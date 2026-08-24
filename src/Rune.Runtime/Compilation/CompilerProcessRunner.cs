using System.ComponentModel;
using System.Diagnostics;
using Rune.Runtime.Exceptions;

namespace Rune.Runtime.Compilation;

public sealed class CompilerProcessRunner
{
    public async ValueTask<CompilerProcessResult> RunAsync(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        Process process;

        try
        {
            process = Process.Start(startInfo)
                ?? throw new RuneCompilationException(
                    $"Could not start '{executable}'.");
        }
        catch (Win32Exception exception)
        {
            throw new RuneCompilationException(
                $"Rune compiler '{executable}' is not installed or is not on PATH.",
                exception);
        }

        using (process)
        {
            var stdoutTask =
                process.StandardOutput.ReadToEndAsync();

            var stderrTask =
                process.StandardError.ReadToEndAsync();

            using var deadline =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            deadline.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(
                    deadline.Token);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    process.Kill(
                        entireProcessTree: true);
                }
                catch
                {
                }

                throw new RuneCompilationException(
                    "Rune compilation timed out.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                var error =
                    string.IsNullOrWhiteSpace(stderr)
                        ? stdout
                        : stderr;

                throw new RuneCompilationException(
                    SanitiseCompilerError(
                        error,
                        workingDirectory));
            }

            return new CompilerProcessResult(
                stdout,
                stderr);
        }
    }

    private static string SanitiseCompilerError(
        string error,
        string temporaryDirectory)
    {
        var cleaned = error.Replace(
            temporaryDirectory,
            "<rune>",
            StringComparison.Ordinal);

        var lines = cleaned
            .Replace("\r\n", "\n")
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(8);

        var result =
            string.Join('\n', lines).Trim();

        if (string.IsNullOrWhiteSpace(result))
            return "Rune compilation failed.";

        return result.Length <= 1200
            ? result
            : result[..1200] + "…";
    }
}
