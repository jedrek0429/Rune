namespace Rune.Core.Runes;

public sealed record BuiltRuneArtifact(
    string Id,
    string Digest,
    string Entrypoint
);

public sealed record RegisteredRune(
    Guid Id,
    ulong GuildId,
    string Name,
    RuneLanguage Language,
    RuneEventType EventType,
    string Source,
    byte[] Wasm,
    bool Enabled,
    BuiltRuneArtifact? Artifact = null
);

public sealed record CompiledRune(
    byte[] Wasm,
    IReadOnlyList<string> Diagnostics,
    RuneEventType EventType = RuneEventType.MessageCreate,
    string ApiFingerprint = ""
);
