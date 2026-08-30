using System.Text.Json;
using Rune.Core.Runes;

namespace Rune.Runtime;

public sealed record RuneInvocationEnvelope(
    Guid ExecutionId,
    Guid InvocationId,
    Guid RuneId,
    string RuneName,
    ulong GuildId,
    RuneLanguage Language,
    RuneEventType EventType,
    string Source,
    JsonElement Payload,
    DateTimeOffset EnqueuedAt,
    BuiltRuneArtifact? Artifact = null);

public sealed record RuneHostActionEnvelope(
    string Method,
    JsonElement Arguments);

public sealed record RuneResultEnvelope(
    Guid ExecutionId,
    Guid InvocationId,
    Guid RuneId,
    string RuneName,
    ulong GuildId,
    RuneLanguage Language,
    RuneEventType EventType,
    JsonElement Payload,
    IReadOnlyList<RuneHostActionEnvelope> Actions,
    string? Error,
    long DurationMicros);

public sealed record RuneResultMessage(
    string StreamId,
    RuneResultEnvelope Result);
