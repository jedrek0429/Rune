using Rune.Api;

namespace Rune.Runtime;

public sealed record RegisteredRune(
    Guid Id,
    ulong GuildId,
    string Name,
    RuneLanguage Language,
    RuneApiEventType EventType,
    string Source,
    bool Enabled,
    BuiltRuneArtifact? Artifact = null);
