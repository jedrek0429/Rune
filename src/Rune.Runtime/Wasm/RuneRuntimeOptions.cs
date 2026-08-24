namespace Rune.Runtime.Wasm;

public sealed class RuneRuntimeOptions
{
    public TimeSpan ExecutionTimeout { get; init; } =
        TimeSpan.FromSeconds(2);

    // 4096 × 64 KiB = 256 MiB.
    // Python may need considerably more baseline memory
    // than JS, so start here and tune after measuring.
    public int MaxMemoryPages { get; init; } = 4096;

    public int MaxConcurrentExecutions { get; init; } = 16;

    public int MaxHostRequestsPerInvocation { get; init; } = 32;

    public int MaxReplyLength { get; init; } = 2000;

    public long? FuelLimit { get; init; }
}
