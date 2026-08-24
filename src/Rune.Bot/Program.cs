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
using Rune.Runtime.Compilation;

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
    .AddRuneCompilation(options =>
    {
        var home =
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);

        options.JavaScriptCompiler =
            Path.Combine(
                home,
                ".local/bin/extism-js");

        options.PythonCompiler =
            Path.Combine(
                home,
                ".local/bin/extism-py");

        options.RustCompiler = "cargo";

        options.JavaScriptTimeout =
            TimeSpan.FromSeconds(30);

        options.PythonTimeout =
            TimeSpan.FromSeconds(30);

        options.RustTimeout =
            TimeSpan.FromMinutes(2);

        options.RustTargetDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "rune-rust-target");
    })
    .AddRuneRuntime(options =>
    {
        options.ExecutionTimeout =
            TimeSpan.FromSeconds(2);

        options.MaxMemoryPages = 4096;
        options.MaxConcurrentExecutions = 16;
        options.MaxHostRequestsPerInvocation = 32;
        options.MaxReplyLength = 2000;
    })
    .AddSingleton<RuneEventDispatcher>()
    .AddSingleton<
        IRuneHostRequestHandler,
        NetCordRuneHostRequestHandler>();

var host = builder.Build();

var registry = host.Services.GetRequiredService<RuneRegistry>();

host.AddModules(typeof(Program).Assembly);

await host.RunAsync();
