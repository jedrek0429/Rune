using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using Rune.Bot;
using Rune.Bot.Host;
using Rune.Core.Runes;
using Rune.Runtime;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddDiscordGateway(options =>
    {
        options.Intents =
            GatewayIntents.Guilds |
            GatewayIntents.GuildMessages |
            GatewayIntents.GuildMessageReactions |
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
        options.ConnectionString =
            Environment.GetEnvironmentVariable("RUNE_REDIS") ??
            "localhost:6379";

        options.InvocationStreamPrefix =
            Environment.GetEnvironmentVariable("RUNE_INVOCATION_STREAM_PREFIX") ??
            "rune:invocations";

        options.ResultStream =
            Environment.GetEnvironmentVariable("RUNE_RESULT_STREAM") ??
            "rune:results";
    })
    .AddSingleton<RuneEventDispatcher>()
    .AddSingleton<IRuneHostRequestHandler, NetCordRuneHostRequestHandler>()
    .AddHostedService<RuneResultWorker>();

var host = builder.Build();

host.AddModules(typeof(Program).Assembly);

await host.RunAsync();
