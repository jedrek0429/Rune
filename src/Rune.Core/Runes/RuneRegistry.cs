namespace Rune.Core.Runes;

public sealed class RuneRegistry
{
    private readonly object _gate = new();

    private readonly Dictionary<Guid, RegisteredRune>
        _runes = [];

    public bool Add(
        RegisteredRune rune)
    {
        lock (_gate)
        {
            if (_runes.Values.Any(existing =>
                existing.GuildId == rune.GuildId &&
                string.Equals(
                    existing.Name,
                    rune.Name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            _runes.Add(
                rune.Id,
                rune);

            return true;
        }
    }

    public RegisteredRune? Get(
        ulong guildId,
        string name)
    {
        lock (_gate)
        {
            return _runes.Values
                .FirstOrDefault(rune =>
                    rune.GuildId == guildId &&
                    string.Equals(
                        rune.Name,
                        name,
                        StringComparison.OrdinalIgnoreCase));
        }
    }

    public IReadOnlyList<RegisteredRune> GetRunes(
        ulong guildId)
    {
        lock (_gate)
        {
            return _runes.Values
                .Where(rune =>
                    rune.GuildId == guildId)
                .OrderBy(rune =>
                    rune.Name)
                .ToArray();
        }
    }

    public IReadOnlyList<RegisteredRune> GetEventRunes(
        ulong guildId,
        RuneEventType eventType)
    {
        lock (_gate)
        {
            return _runes.Values
                .Where(rune =>
                    rune.GuildId == guildId &&
                    rune.EventType == eventType &&
                    rune.Enabled)
                .ToArray();
        }
    }

    public bool Remove(
        ulong guildId,
        string name,
        out RegisteredRune? rune)
    {
        lock (_gate)
        {
            rune = _runes.Values
                .FirstOrDefault(candidate =>
                    candidate.GuildId == guildId &&
                    string.Equals(
                        candidate.Name,
                        name,
                        StringComparison.OrdinalIgnoreCase));

            if (rune is null)
                return false;

            return _runes.Remove(rune.Id);
        }
    }

    public bool SetEnabled(
        ulong guildId,
        string name,
        bool enabled,
        out RegisteredRune? rune)
    {
        lock (_gate)
        {
            var current =
                _runes.Values
                    .FirstOrDefault(candidate =>
                        candidate.GuildId == guildId &&
                        string.Equals(
                            candidate.Name,
                            name,
                            StringComparison.OrdinalIgnoreCase));

            if (current is null)
            {
                rune = null;
                return false;
            }

            rune = current with
            {
                Enabled = enabled
            };

            _runes[current.Id] =
                rune;

            return true;
        }
    }

    public void Replace(
        RegisteredRune rune)
    {
        lock (_gate)
        {
            _runes[rune.Id] =
                rune;
        }
    }
}
