using Microsoft.Extensions.DependencyInjection;

namespace Rune.Runtime;

public static class DependencyInjection
{
    public static IServiceCollection AddRuneRuntime(
        this IServiceCollection services,
        Action<RuneRuntimeOptions>? configure = null)
    {
        var options = new RuneRuntimeOptions();
        configure?.Invoke(options);

        services
            .AddSingleton(options)
            .AddSingleton<IRuneBuilder, FirecrackerRuneBuilder>()
            .AddSingleton<IRuneTransport, RedisRuneTransport>();

        return services;
    }
}
