namespace Rune.Core.Runes;

public sealed record RegisteredRune(
    Guid Id,
    ulong GuildId,
    string Name,
    RuneLanguage Language,
    RuneEventType EventType,
    string Source,
    byte[] Wasm,
    bool Enabled
);
