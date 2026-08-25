using Microsoft.Extensions.DependencyInjection;
using Rune.Runtime.Native;

namespace Rune.Runtime;

public static class DependencyInjection
{
    public static IServiceCollection AddRuneRuntime(
        this IServiceCollection services)
    {
        services.AddSingleton<RuneNativeRuntime>();
        services.AddSingleton<IRuneComponentRuntime>(provider =>
            provider.GetRequiredService<RuneNativeRuntime>());

        return services;
    }
}
