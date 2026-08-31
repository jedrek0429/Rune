using System.Text;
using Rune.Core.Runes;

namespace Rune.Runtime;

public sealed class RuneService(
    RuneRegistry runeRegistry,
    IRuneBuilder builder)
{
    public async ValueTask<RegisteredRune> RegisterAsync(
        ulong guildId,
        string name,
        RuneLanguage language,
        RuneEventType eventType,
        string source,
        CancellationToken cancellationToken = default)
    {
        ValidateSource(source);
        if (runeRegistry.Get(guildId, name) is not null)
            throw new InvalidOperationException($"A rune named '{name}' already exists.");

        var artifact = await builder.BuildAsync(language, source, cancellationToken);
        ValidateArtifact(artifact);

        var rune = new RegisteredRune(
            Guid.NewGuid(), guildId, name, language, eventType,
            source, true, artifact);

        if (runeRegistry.Add(rune))
            return rune;

        throw new InvalidOperationException($"A rune named '{name}' already exists.");
    }

    public async ValueTask<RegisteredRune> UpdateAsync(
        RegisteredRune current,
        RuneLanguage language,
        string source,
        CancellationToken cancellationToken = default)
    {
        ValidateSource(source);
        var artifact = await builder.BuildAsync(language, source, cancellationToken);
        ValidateArtifact(artifact);

        var updated = current with
        {
            Language = language,
            Source = source,
            Artifact = artifact
        };

        runeRegistry.Replace(updated);
        return updated;
    }

    public ValueTask<RegisteredRune?> RemoveAsync(
        ulong guildId,
        string name) =>
        ValueTask.FromResult(
            runeRegistry.Remove(guildId, name, out var removed)
                ? removed
                : null);

    public ValueTask<RegisteredRune?> SetEnabledAsync(
        ulong guildId,
        string name,
        bool enabled)
    {
        var current = runeRegistry.Get(guildId, name);
        if (current is null || current.Enabled == enabled)
            return ValueTask.FromResult(current);

        return ValueTask.FromResult(
            runeRegistry.SetEnabled(guildId, name, enabled, out var updated)
                ? updated
                : null);
    }

    private static void ValidateSource(string source)
    {
        if (Encoding.UTF8.GetByteCount(source) > RuneResourceLimits.MaxSourceBytes)
            throw new InvalidOperationException("Rune source may not exceed 64 KiB.");
    }

    private static void ValidateArtifact(BuiltRuneArtifact artifact)
    {
        if (artifact.SizeBytes < 0 || artifact.SizeBytes > RuneResourceLimits.MaxArtifactBytes)
            throw new InvalidOperationException("Built Rune artifact may not exceed 16 MiB.");

        if (string.IsNullOrWhiteSpace(artifact.Id) ||
            string.IsNullOrWhiteSpace(artifact.Digest) ||
            string.IsNullOrWhiteSpace(artifact.Entrypoint))
        {
            throw new InvalidOperationException("Built Rune artifact descriptor is incomplete.");
        }
    }
}
