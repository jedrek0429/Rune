namespace Rune.Runtime;

public sealed class RuneRuntimeOptions
{
    public string RedisConnectionString { get; set; } =
        "localhost:6379";

    public string ResultConsumerGroup { get; set; } =
        "rune-bot";

    public int ResultBatchSize { get; set; } =
        32;
}
