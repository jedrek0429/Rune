using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using Rune.Bot.Host;
using Rune.Core.Runes;
using Rune.Runtime;
using Rune.Runtime.Compilation;
using Rune.Runtime.Wasm;

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
    .AddSingleton(
        new RuneCompilerOptions
        {
            JavaScriptCompiler = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                ".local/bin/extism-js"),

            PythonCompiler = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                ".local/bin/extism-py"),

            Timeout = TimeSpan.FromSeconds(30)
        })
    .AddSingleton(
        new RuneRuntimeOptions
        {
            ExecutionTimeout =
                TimeSpan.FromSeconds(2),

            MaxMemoryPages = 4096,
            MaxConcurrentExecutions = 16,
            MaxHostRequestsPerInvocation = 32,
            MaxReplyLength = 2000
        })
    .AddSingleton<RuneCompiler>()
    .AddSingleton<RuneWasmCache>()
    .AddSingleton<RuneExecutor>()
    .AddSingleton<RuneEventDispatcher>()
    .AddSingleton<
        IRuneHostRequestHandler,
        NetCordRuneHostRequestHandler>();

var host = builder.Build();

var registry = host.Services.GetRequiredService<RuneRegistry>();

host.AddModules(typeof(Program).Assembly);

await host.RunAsync();
