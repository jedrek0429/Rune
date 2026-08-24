namespace Rune.Runtime;

public sealed class RuneRuntimeOptions
{
    public TimeSpan ExecutionTimeout { get; set; } =
        TimeSpan.FromSeconds(2);

    public int MaxMemoryPages { get; set; } = 4096;

    public int MaxConcurrentExecutions { get; set; } = 16;

    public int MaxHostRequestsPerInvocation { get; set; } = 32;

    public int MaxReplyLength { get; set; } = 2000;

    public long? FuelLimit { get; init; }
}
