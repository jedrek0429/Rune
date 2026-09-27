using System.Text.Json;

using Rune.Core.Runes;

namespace Rune.Core.Invocations;

public sealed record RuneInvocationEnvelope(
    Guid ExecutionId,
    Guid InvocationId,
    Guid RuneId,
    string RuneName,
    ulong GuildId,
    RuneEventType EventType,
    BuiltRuneArtifact Artifact,
    JsonElement Payload,
    DateTimeOffset EnqueuedAt
);
