using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Rune.Generator;

public sealed record RuntimeContract(
    string InvocationStream,
    string ResultStream,
    string RunnerConsumerGroup,
    long MaxArtifactBytes,
    long MaxInvocationBytes,
    long MaxResultBytes,
    IReadOnlyList<RuntimeContractType> Types);

public sealed record RuntimeContractType(
    string Name,
    IReadOnlyList<RuntimeContractField> Fields);

public sealed record RuntimeContractField(
    string Name,
    string Type);

public static class RuntimeContractLoader
{
    public static RuntimeContract Load(
        string path)
    {
        var manifest =
            new DeserializerBuilder()
                .WithNamingConvention(
                    HyphenatedNamingConvention.Instance)
                .Build()
                .Deserialize<Manifest>(
                    File.ReadAllText(path));

        if (manifest.Schema != 1)
            throw new InvalidOperationException(
                "Runtime contract schema must be 1.");

        return new RuntimeContract(
            manifest.Streams.Invocations,
            manifest.Streams.Results,
            manifest.Streams.RunnerConsumerGroup,
            manifest.Limits.MaxArtifactBytes,
            manifest.Limits.MaxInvocationBytes,
            manifest.Limits.MaxResultBytes,
            manifest.Types
                .Select(pair =>
                    new RuntimeContractType(
                        pair.Key,
                        pair.Value.Fields
                            .Select(field =>
                                new RuntimeContractField(
                                    field.Name,
                                    field.Type))
                            .ToArray()))
                .ToArray());
    }

    private sealed class Manifest
    {
        public int Schema { get; init; }
        public StreamsManifest Streams { get; init; } = new();
        public LimitsManifest Limits { get; init; } = new();
        public Dictionary<string, TypeManifest> Types { get; init; } = [];
    }

    private sealed class StreamsManifest
    {
        public string Invocations { get; init; } = string.Empty;
        public string Results { get; init; } = string.Empty;
        public string RunnerConsumerGroup { get; init; } = string.Empty;
    }

    private sealed class LimitsManifest
    {
        public long MaxArtifactBytes { get; init; }
        public long MaxInvocationBytes { get; init; }
        public long MaxResultBytes { get; init; }
    }

    private sealed class TypeManifest
    {
        public List<FieldManifest> Fields { get; init; } = [];
    }

    private sealed class FieldManifest
    {
        public string Name { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
    }
}
