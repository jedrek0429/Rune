using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Rune.Api.Generator;

public sealed record RuneApiModel(
    string Package,
    string Version,
    string NetCordVersion,
    IReadOnlyList<RuneApiType> Types,
    IReadOnlyList<RuneApiEvent> Events,
    string Fingerprint);

public sealed record RuneApiType(
    string Name,
    string NetCordName,
    bool IsEnum,
    string? Base,
    IReadOnlyList<RuneApiMember> Members);

public sealed record RuneApiMember(
    string Name,
    string CanonicalId,
    RuneApiValueType Type,
    string? Representation,
    long? EnumValue);

public sealed record RuneApiValueType(
    string Name,
    bool Optional,
    bool IsSelectedType);

public sealed record RuneApiEvent(
    string Name,
    string NetCordName,
    string Payload,
    string World);

public sealed class RuneApiValidationException(string message)
    : Exception(message);

public static class RuneApiLoader
{
    private static readonly NullabilityInfoContext Nullability = new();

    public static RuneApiModel Load(string path) =>
        LoadText(File.ReadAllText(path));

    public static RuneApiModel LoadText(string source)
    {
        Manifest manifest;

        try
        {
            manifest = new DeserializerBuilder()
                .WithNamingConvention(HyphenatedNamingConvention.Instance)
                .Build()
                .Deserialize<Manifest>(source);
        }
        catch (Exception exception)
        {
            throw new RuneApiValidationException(
                $"Rune.API manifest is invalid YAML: {exception.Message}");
        }

        ValidateManifest(manifest);

        var netCordAssembly = typeof(NetCord.User).Assembly;
        var selectedNames = manifest.Types.Keys.ToHashSet(StringComparer.Ordinal);
        var selectedByNetCord = manifest.Types.ToDictionary(
            pair => pair.Value.NetCord,
            pair => pair.Key,
            StringComparer.Ordinal);
        var types = new List<RuneApiType>();

        foreach (var (name, selection) in manifest.Types)
        {
            var runtimeType = netCordAssembly.GetType(selection.NetCord)
                ?? throw new RuneApiValidationException(
                    $"NetCord type '{selection.NetCord}' selected as '{name}' does not exist.");

            if (selection.Base is not null)
            {
                if (!selectedNames.Contains(selection.Base))
                {
                    throw new RuneApiValidationException(
                        $"Base projection '{selection.Base}' for '{name}' is not selected.");
                }

                var baseNetCord = manifest.Types[selection.Base].NetCord;
                var baseType = netCordAssembly.GetType(baseNetCord)!;
                if (!baseType.IsAssignableFrom(runtimeType))
                {
                    throw new RuneApiValidationException(
                        $"NetCord type '{selection.NetCord}' does not inherit '{baseNetCord}'.");
                }
            }

            var members = runtimeType.IsEnum
                ? LoadEnumMembers(runtimeType, selection)
                : LoadPropertyMembers(runtimeType, selection, selectedByNetCord);

            types.Add(new RuneApiType(
                name,
                selection.NetCord,
                runtimeType.IsEnum,
                selection.Base,
                members));
        }

        var events = new List<RuneApiEvent>();
        foreach (var (name, selection) in manifest.Events)
        {
            if (!selectedNames.Contains(selection.Payload))
            {
                throw new RuneApiValidationException(
                    $"Event '{name}' uses unselected payload '{selection.Payload}'.");
            }

            ValidateEvent(netCordAssembly, selection.NetCord, manifest.Types[selection.Payload]);
            events.Add(new RuneApiEvent(
                name,
                selection.NetCord,
                selection.Payload,
                $"{Names.Kebab(name)}-rune"));
        }

        var fingerprintSource = string.Join(
            '\n',
            types.SelectMany(type =>
                new[] { $"type:{type.NetCordName}" }.Concat(
                    type.Members.Select(member =>
                        $"member:{member.CanonicalId}:{member.Type.Name}:" +
                        $"{member.Type.Optional}:{member.Representation}:{member.EnumValue}")))
                .Concat(events.Select(value =>
                    $"event:{value.NetCordName}:{value.Payload}:{value.World}")));
        var fingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintSource)))
            .ToLowerInvariant();

        return new RuneApiModel(
            manifest.Api.Package,
            manifest.Api.Version,
            manifest.NetCord.Version,
            types,
            events,
            fingerprint);
    }

    private static IReadOnlyList<RuneApiMember> LoadPropertyMembers(
        Type runtimeType,
        TypeSelection selection,
        IReadOnlyDictionary<string, string> selectedByNetCord)
    {
        var members = new List<RuneApiMember>();

        foreach (var selected in selection.Members)
        {
            var property = runtimeType.GetProperty(
                selected.Name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy)
                ?? throw new RuneApiValidationException(
                    $"Member '{selected.Name}' does not exist on '{runtimeType.FullName}'.");

            var nullableReference = !property.PropertyType.IsValueType &&
                Nullability.Create(property).ReadState == NullabilityState.Nullable;
            var valueType = ToValueType(
                property.PropertyType,
                selectedByNetCord,
                nullableReference);
            if (selected.Representation == "snowflake" &&
                valueType.Name != "u64")
            {
                throw new RuneApiValidationException(
                    $"Member '{runtimeType.FullName}.{selected.Name}' cannot use snowflake " +
                    $"representation because its type is '{property.PropertyType}'.");
            }

            members.Add(new RuneApiMember(
                selected.Name,
                $"{runtimeType.FullName}.{selected.Name}",
                valueType,
                selected.Representation,
                null));
        }

        return members;
    }

    private static IReadOnlyList<RuneApiMember> LoadEnumMembers(
        Type runtimeType,
        TypeSelection selection)
    {
        var members = new List<RuneApiMember>();

        foreach (var selected in selection.Members)
        {
            if (!Enum.GetNames(runtimeType).Contains(selected.Name, StringComparer.Ordinal))
            {
                throw new RuneApiValidationException(
                    $"Enum value '{selected.Name}' does not exist on '{runtimeType.FullName}'.");
            }

            var value = Convert.ToInt64(Enum.Parse(runtimeType, selected.Name));
            members.Add(new RuneApiMember(
                selected.Name,
                $"{runtimeType.FullName}.{selected.Name}",
                new RuneApiValueType(runtimeType.Name, false, true),
                null,
                value));
        }

        return members;
    }

    private static RuneApiValueType ToValueType(
        Type source,
        IReadOnlyDictionary<string, string> selectedByNetCord,
        bool nullableReference)
    {
        var optional = nullableReference;
        var underlying = Nullable.GetUnderlyingType(source);
        if (underlying is not null)
        {
            optional = true;
            source = underlying;
        }

        if (source == typeof(string))
            return new RuneApiValueType("string", optional, false);
        if (source == typeof(bool))
            return new RuneApiValueType("bool", optional, false);
        if (source == typeof(ulong))
            return new RuneApiValueType("u64", optional, false);
        if (source.FullName is not null &&
            selectedByNetCord.TryGetValue(source.FullName, out var selected))
        {
            return new RuneApiValueType(selected, optional, true);
        }

        throw new RuneApiValidationException(
            $"NetCord type '{source.FullName}' has no selected portable lowering.");
    }

    private static void ValidateEvent(
        Assembly assembly,
        string canonicalName,
        TypeSelection payload)
    {
        var separator = canonicalName.LastIndexOf('.');
        if (separator < 1)
        {
            throw new RuneApiValidationException(
                $"Event identity '{canonicalName}' is invalid.");
        }

        var declaringName = canonicalName[..separator];
        var eventName = canonicalName[(separator + 1)..];
        var declaringType = assembly.GetType(declaringName)
            ?? throw new RuneApiValidationException(
                $"Event declaring type '{declaringName}' does not exist.");
        var eventInfo = declaringType.GetEvent(eventName)
            ?? throw new RuneApiValidationException(
                $"Event '{canonicalName}' does not exist.");
        var payloadType = assembly.GetType(payload.NetCord)!;
        var invoke = eventInfo.EventHandlerType?.GetMethod("Invoke");

        if (invoke is null ||
            !invoke.GetParameters().Any(parameter =>
                parameter.ParameterType == payloadType))
        {
            throw new RuneApiValidationException(
                $"Event '{canonicalName}' does not deliver '{payload.NetCord}'.");
        }
    }

    private static void ValidateManifest(Manifest manifest)
    {
        if (manifest.Schema != 1)
            throw new RuneApiValidationException("Rune.API schema must be 1.");
        if (string.IsNullOrWhiteSpace(manifest.Api.Package) ||
            string.IsNullOrWhiteSpace(manifest.Api.Version))
        {
            throw new RuneApiValidationException("Rune.API package and version are required.");
        }

        if (manifest.NetCord.Package != "NetCord" ||
            manifest.NetCord.Version != "1.0.0-beta.16")
        {
            throw new RuneApiValidationException(
                "Rune.API must select the NetCord 1.0.0-beta.16 package used by Rune.Bot.");
        }

        if (manifest.Types.Count == 0 || manifest.Events.Count == 0)
            throw new RuneApiValidationException("Rune.API types and events are required.");
    }

    private sealed class Manifest
    {
        public int Schema { get; init; }
        public PackageSelection Api { get; init; } = new();
        [YamlMember(Alias = "netcord")]
        public PackageSelection NetCord { get; init; } = new();
        public Dictionary<string, TypeSelection> Types { get; init; } = [];
        public Dictionary<string, EventSelection> Events { get; init; } = [];
    }

    private sealed class PackageSelection
    {
        public string Package { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
    }

    private sealed class TypeSelection
    {
        [YamlMember(Alias = "netcord")]
        public string NetCord { get; init; } = string.Empty;
        public string? Base { get; init; }
        public List<MemberSelection> Members { get; init; } = [];
    }

    private sealed class MemberSelection
    {
        public string Name { get; init; } = string.Empty;
        public string? Representation { get; init; }
    }

    private sealed class EventSelection
    {
        [YamlMember(Alias = "netcord")]
        public string NetCord { get; init; } = string.Empty;
        public string Payload { get; init; } = string.Empty;
    }
}

internal static class Names
{
    internal static string Kebab(string value) => Separate(value, '-');
    internal static string Snake(string value) => Separate(value, '_');

    internal static string Camel(string value) =>
        value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value[1..];

    private static string Separate(string value, char separator)
    {
        var output = new StringBuilder();
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (index > 0 && char.IsUpper(current) &&
                (!char.IsUpper(value[index - 1]) ||
                 (index + 1 < value.Length && char.IsLower(value[index + 1]))))
            {
                output.Append(separator);
            }

            output.Append(char.ToLowerInvariant(current));
        }

        return output.ToString();
    }
}
