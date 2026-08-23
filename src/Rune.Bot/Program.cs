using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using Rune.Bot.Host;
using Rune.Core.Runes;
using Rune.Runtime;
using Rune.Runtime.Sandbox;
using Rune.Runtime.Sandbox.Local;

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
    .AddSingleton<RuneEventDispatcher>()
    .AddSingleton<ISandboxBackend, LocalSandboxBackend>()
    .AddSingleton<SandboxManager>()
    .AddSingleton<
        IRuneHostRequestHandler,
        NetCordRuneHostRequestHandler>();

var host = builder.Build();

var registry = host.Services.GetRequiredService<RuneRegistry>();

host.AddModules(typeof(Program).Assembly);

await host.RunAsync();
