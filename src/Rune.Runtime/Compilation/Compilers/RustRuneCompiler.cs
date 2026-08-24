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
                CargoManifest,
                Encoding.UTF8,
                cancellationToken);

            await File.WriteAllTextAsync(
                input,
                BuildWrapper(source),
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
                        "wasm32-unknown-unknown",
                        "--target-dir",
                        options.RustTargetDirectory
                    ],
                    options.RustTimeout,
                    cancellationToken);

            var output =
                Path.Combine(
                    options.RustTargetDirectory,
                    "wasm32-unknown-unknown",
                    "release",
                    "rune.wasm");

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

    private static string BuildWrapper(
        string source)
    {
    return $$"""
use extism_pdk::*;
use serde::Deserialize;

#[derive(Debug, Deserialize)]
pub struct RuneInvocation {
    pub message: RuneMessage,
}

#[derive(Debug, Deserialize)]
pub struct RuneUser {
    pub id: u64,
    pub username: String,
}

#[derive(Debug, Deserialize)]
pub struct RuneMessage {
    pub id: u64,

    #[serde(rename = "channelId")]
    pub channel_id: u64,

    pub content: String,
    pub author: RuneUser,
}

#[host_fn]
extern "ExtismHost" {
    fn rune_message_reply(content: String);
}

impl RuneMessage {
    pub fn reply(
        &self,
        content: impl Into<String>)
        -> FnResult<()>
    {
        unsafe {
            rune_message_reply(
                content.into())?;
        }

        Ok(())
    }
}

{{source}}

#[plugin_fn]
pub fn handle(_: ()) -> FnResult<()> {
    let Json(invocation): Json<RuneInvocation> =
        input()?;

    rune(invocation.message)
}
""";
    }

    private const string CargoManifest = """
[package]
name = "rune"
version = "0.0.0"
edition = "2021"

[lib]
crate-type = ["cdylib"]

[dependencies]
extism-pdk = "1"
serde = { version = "1", features = ["derive"] }
""";
}
