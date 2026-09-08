using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using Rune.Bot;
using Rune.Core.Runes;
using Rune.Runtime;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddDiscordGateway(options =>
    {
        options.Intents =
            GatewayIntents.Guilds |
            GatewayIntents.GuildMessages |
            GatewayIntents.MessageContent;
    })
    .AddHttpClient()
    .AddApplicationCommands()
    .AddGatewayHandlers(typeof(Program).Assembly);

builder.Services
    .AddSingleton<RuneRegistry>()
    .AddSingleton<RuneService>()
    .AddSingleton<RuneUploadReader>()
    .AddRuneRuntime(options =>
    {
        options.RedisConnectionString =
            Environment.GetEnvironmentVariable("RUNE_REDIS_URL") ?? "localhost:6379";
    })
    .AddSingleton<RuneEventDispatcher>();

var host = builder.Build();

host.AddModules(typeof(Program).Assembly);

await host.RunAsync();
