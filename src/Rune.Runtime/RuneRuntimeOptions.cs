namespace Rune.Runtime;

public sealed class RuneRuntimeOptions
{
    public string RedisConnectionString { get; set; } = "localhost:6379";
    public string InvocationStream { get; set; } = "rune:invocations";
}
