namespace Rune.Core.Runes;

public sealed record BuiltRuneArtifact(
    string Id,
    string Digest,
    string Entrypoint,
    long SizeBytes
);

public sealed record RegisteredRune(
    Guid Id,
    ulong GuildId,
    string Name,
    RuneLanguage Language,
    RuneEventType EventType,
    string Source,
    bool Enabled,
    BuiltRuneArtifact? Artifact = null
);
