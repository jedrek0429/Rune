using System.Text.Json;

namespace Rune.Runtime;

public sealed record RuneHostAction(
    string Method,
    JsonElement Arguments);

public sealed record RuneResultEnvelope(
    Guid ExecutionId,
    Guid InvocationId,
    Guid RuneId,
    IReadOnlyList<RuneHostAction> Actions,
    string? Error);

public sealed record RuneResultMessage(
    string StreamId,
    RuneResultEnvelope Result);
