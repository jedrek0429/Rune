namespace Rune.Runtime;

public sealed class RuneRedisOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";

    public string InvocationStream { get; set; } = "rune:invocations";

    public string ResultStream { get; set; } = "rune:results";

    public string ResultConsumerGroup { get; set; } = "rune-bot";

    public int ResultBatchSize { get; set; } = 32;

    public int MaxResultStreamLength { get; set; } = 10_000;
}
