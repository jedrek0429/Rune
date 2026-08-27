namespace Rune.Runtime;

public sealed class RuneRedisOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";

    public string InvocationStreamPrefix { get; set; } = "rune:invocations";

    public string ResultStream { get; set; } = "rune:results";

    public string ResultConsumerGroup { get; set; } = "rune-bot";

    public int ResultBatchSize { get; set; } = 32;

    public int MaxResultStreamLength { get; set; } = 10_000;

    public string GetInvocationStream(Core.Runes.RuneLanguage language) =>
        $"{InvocationStreamPrefix}:{language.ToString().ToLowerInvariant()}";
}
