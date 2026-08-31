namespace Rune.Core.Runes;

public sealed record BuiltRuneArtifact(
    string Id,
    string Digest,
    string Entrypoint,
    long SizeBytes
);
