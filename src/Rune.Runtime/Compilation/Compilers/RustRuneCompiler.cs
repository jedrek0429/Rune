using System.Text;
using Rune.Core.Runes;

namespace Rune.Runtime.Compilation.Compilers;

public sealed class RustRuneCompiler(
    RuneCompilationOptions options,
    CompilerProcessRunner processRunner,
    WasmPipeline wasmPipeline)
    : ILanguageCompiler
{
    public RuneLanguage Language =>
        RuneLanguage.Rust;

    public async ValueTask<CompiledRune> CompileAsync(
        RuneEventType eventType,
        string source,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"rune-{Guid.NewGuid():N}");

        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(
            options.RustTargetDirectory);

        try
        {
            var srcDirectory =
                Path.Combine(
                    directory,
                    "src");

            Directory.CreateDirectory(
                srcDirectory);

            var witDirectory = Path.Combine(directory, "wit");
            Directory.CreateDirectory(witDirectory);

            await File.WriteAllTextAsync(
                Path.Combine(witDirectory, "rune-api.wit"),
                await File.ReadAllTextAsync(
                    options.RuneApiWitPath,
                    cancellationToken),
                Encoding.UTF8,
                cancellationToken);

            var packageName = $"rune_{Guid.NewGuid():N}";

            var manifest =
                Path.Combine(
                    directory,
                    "Cargo.toml");

            var input =
                Path.Combine(
                    srcDirectory,
                    "lib.rs");

            await File.WriteAllTextAsync(
                manifest,
                CargoManifest(packageName),
                Encoding.UTF8,
                cancellationToken);

            await File.WriteAllTextAsync(
                input,
                await BuildWrapperAsync(
                    options.GeneratedApiRoot,
                    eventType,
                    source,
                    cancellationToken),
                Encoding.UTF8,
                cancellationToken);

            var result =
                await processRunner.RunAsync(
                    options.RustCompiler,
                    directory,
                    [
                        "build",
                        "--release",
                        "--target",
                        "wasm32-wasip2",
                        "--target-dir",
                        options.RustTargetDirectory
                    ],
                    options.RustTimeout,
                    cancellationToken);

            var output =
                Path.Combine(
                    options.RustTargetDirectory,
                    "wasm32-wasip2",
                    "release",
                    $"{packageName}.wasm");

            var diagnostics =
                new[]
                {
                    result.StandardOutput,
                    result.StandardError
                }
                .Where(
                    value =>
                        !string.IsNullOrWhiteSpace(
                            value))
                .ToArray();

            return await wasmPipeline.ProcessAsync(
                output,
                eventType,
                diagnostics,
                cancellationToken);
        }
        finally
        {
            try
            {
                Directory.Delete(
                    directory,
                    recursive: true);
            }
            catch
            {
            }
        }
    }

    private static async ValueTask<string> BuildWrapperAsync(
        string generatedApiRoot,
        RuneEventType eventType,
        string source,
        CancellationToken cancellationToken)
    {
        var argumentType = eventType switch
        {
            RuneEventType.MessageCreate => "Message",
            RuneEventType.MessageDelete => "MessageDeleteEventArgs",
            RuneEventType.MessageReactionAdd =>
                "MessageReactionAddEventArgs",
            RuneEventType.MessageReactionRemove =>
                "MessageReactionRemoveEventArgs",
            _ => throw new ArgumentOutOfRangeException(
                nameof(eventType),
                eventType,
                "The gateway event is not supported.")
        };
        var facade = await File.ReadAllTextAsync(
            Path.Combine(generatedApiRoot, "rust", "rune_api.rs"),
            cancellationToken);

        return $$"""
mod bindings {
    wit_bindgen::generate!({
        path: "wit",
        world: "{{World(eventType)}}",
    });
}

{{facade}}

{{source}}

struct Component;

impl bindings::Guest for Component {
    fn handle(argument: {{argumentType}}) {
        rune(argument);
    }
}

bindings::export!(Component with_types_in bindings);
""";
    }

    private static string CargoManifest(string packageName) => $$"""
[package]
name = "{{packageName}}"
version = "0.0.0"
edition = "2021"

[lib]
crate-type = ["cdylib"]

[dependencies]
wit-bindgen = "0.60.0"
""";

    private static string World(RuneEventType eventType) =>
        eventType switch
        {
            RuneEventType.MessageCreate => "message-create-rune",
            RuneEventType.MessageDelete => "message-delete-rune",
            RuneEventType.MessageReactionAdd => "message-reaction-add-rune",
            RuneEventType.MessageReactionRemove => "message-reaction-remove-rune",
            _ => throw new ArgumentOutOfRangeException(nameof(eventType))
        };
}
