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
    .AddRuneCompilation(options =>
    {
        options.JavaScriptCompiler =
            Environment.GetEnvironmentVariable("RUNE_JCO") ??
            "jco";

        options.PythonCompiler =
            Environment.GetEnvironmentVariable("RUNE_COMPONENTIZE_PY") ??
            "componentize-py";

        options.RustCompiler =
            Environment.GetEnvironmentVariable("RUNE_CARGO") ??
            "cargo";

        options.RuneApiWitPath =
            Path.Combine(
                builder.Environment.ContentRootPath,
                "wit",
                "rune-api.wit");

        options.GeneratedApiRoot =
            Path.Combine(
                builder.Environment.ContentRootPath,
                "generated");

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
    .AddRuneRuntime()
    .AddSingleton<RuneEventDispatcher>()
    .AddSingleton<
        IRuneHostRequestHandler,
        NetCordRuneHostRequestHandler>();

var host = builder.Build();

var registry = host.Services.GetRequiredService<RuneRegistry>();

host.AddModules(typeof(Program).Assembly);

await host.RunAsync();
