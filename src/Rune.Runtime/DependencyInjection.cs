using Microsoft.Extensions.DependencyInjection;

namespace Rune.Runtime;

public static class DependencyInjection
{
    public static IServiceCollection AddRuneRuntime(
        this IServiceCollection services,
        Action<RuneRedisOptions>? configure = null)
    {
        var options = new RuneRedisOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IRuneTransport, RedisRuneTransport>();

        return services;
    }
}
