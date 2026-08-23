using System.Diagnostics;
using System.Text.Json;

using Rune.Core.Runes;
using Rune.Runtime.Sandbox;

namespace Rune.Runtime.Sandbox.Local;

public sealed class LocalSandboxBackend : ISandboxBackend
{
    public async ValueTask<ISandbox> StartAsync(
        RegisteredRune rune,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        switch (rune.Language)
        {
            case RuneLanguage.JavaScript:
                startInfo.FileName = "node";
                startInfo.ArgumentList.Add(
                    "runtimes/javascript/runtime.mjs");
                break;

            case RuneLanguage.Python:
                startInfo.FileName = "python3";
                startInfo.ArgumentList.Add(
                    "runtimes/python/runtime.py");
                break;

            default:
                throw new NotSupportedException(
                    $"Language '{rune.Language}' has no local runtime.");
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Failed to start {rune.Language} runtime.");

        _ = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
                Console.Error.WriteLine($"[{rune.Language} sandbox] {line}");
        });

        var sandbox = new LocalSandbox(process);

        var load = JsonSerializer.Serialize(new
        {
            type = "load",
            source = rune.Source
        });

        await sandbox.SendAsync(
            load,
            cancellationToken);

        await foreach (
            var line in sandbox.ReadAsync(cancellationToken))
        {
            using var document = JsonDocument.Parse(line);

            var type = document.RootElement
                .GetProperty("type")
                .GetString();

            if (type == "ready")
                return sandbox;

            if (type == "load_error")
            {
                await sandbox.DisposeAsync();

                throw new InvalidOperationException(
                    $"Rune '{rune.Name}' could not be loaded.");
            }
        }

        await sandbox.DisposeAsync();

        throw new InvalidOperationException(
            $"Sandbox for rune '{rune.Name}' exited before becoming ready.");
    }
}
