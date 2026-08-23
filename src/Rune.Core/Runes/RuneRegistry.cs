using System.Collections.Concurrent;

namespace Rune.Core.Runes;

public sealed class RuneRegistry
{
    private readonly ConcurrentDictionary<Guid, RegisteredRune> _runes = [];

    public void Add(RegisteredRune rune)
    {
        if (!_runes.TryAdd(rune.Id, rune))
            throw new InvalidOperationException(
                $"Rune {rune.Id} is already registered.");
    }

    public IReadOnlyList<RegisteredRune> GetEventRunes(
        ulong guildId,
        RuneEventType eventType)
    {
        return _runes.Values
            .Where(rune =>
                rune.GuildId == guildId &&
                rune.EventType == eventType &&
                rune.Enabled)
            .ToArray();
    }

    public IReadOnlyList<RegisteredRune> GetRunes(
        ulong guildId)
    {
        return _runes.Values
            .Where(rune => rune.GuildId == guildId)
            .ToArray();
    }

    public bool Exists(
        ulong guildId,
        string name)
    {
        return _runes.Values.Any(rune =>
            rune.GuildId == guildId &&
            string.Equals(
                rune.Name,
                name,
                StringComparison.OrdinalIgnoreCase));
    }
}
