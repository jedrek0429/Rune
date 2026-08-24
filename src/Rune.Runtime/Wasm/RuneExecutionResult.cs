using Rune.Runtime;

namespace Rune.Runtime.Wasm;

public sealed record RuneExecutionResult(
    IReadOnlyList<RuneHostRequest> Requests);
