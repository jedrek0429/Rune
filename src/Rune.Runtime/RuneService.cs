using Rune.Core.Runes;

namespace Rune.Runtime;

public sealed class RuneService(RuneRegistry runeRegistry)
{
    public ValueTask<RegisteredRune> RegisterAsync(
        ulong guildId,
        string name,
        RuneLanguage language,
        RuneEventType eventType,
        string source,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (runeRegistry.Get(guildId, name) is not null)
            throw new InvalidOperationException($"A rune named '{name}' already exists.");

        var rune = new RegisteredRune(
            Guid.NewGuid(),
            guildId,
            name,
            language,
            eventType,
            source,
            true);

        if (runeRegistry.Add(rune))
            return ValueTask.FromResult(rune);

        throw new InvalidOperationException($"A rune named '{name}' already exists.");
    }

    public ValueTask<RegisteredRune> UpdateAsync(
        RegisteredRune current,
        RuneLanguage language,
        string source,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var updated = current with
        {
            Language = language,
            Source = source,
            Artifact = null
        };

        runeRegistry.Replace(updated);
        return ValueTask.FromResult(updated);
    }

    public ValueTask<RegisteredRune?> RemoveAsync(
        ulong guildId,
        string name)
    {
        return ValueTask.FromResult(
            runeRegistry.Remove(guildId, name, out var removed)
                ? removed
                : null);
    }

    public ValueTask<RegisteredRune?> SetEnabledAsync(
        ulong guildId,
        string name,
        bool enabled)
    {
        var current = runeRegistry.Get(guildId, name);
        if (current is null)
            return ValueTask.FromResult<RegisteredRune?>(null);

        if (current.Enabled == enabled)
            return ValueTask.FromResult<RegisteredRune?>(current);

        return ValueTask.FromResult(
            runeRegistry.SetEnabled(guildId, name, enabled, out var updated)
                ? updated
                : null);
    }
}
