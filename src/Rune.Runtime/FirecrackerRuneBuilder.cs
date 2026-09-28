using System.Diagnostics;

using Rune.Runtime.Exceptions;

namespace Rune.Runtime;

public sealed class FirecrackerRuneBuilder(
    string scriptPath = "src/Rune.Firecracker/run-build-vm.sh")
    : IRuneBuilder
{
    public static (string Pool, string Language)
        GetBuildTarget(
            RuneLanguage language) =>
        language switch
        {
            RuneLanguage.JavaScript =>
                ("scriptc", "javascript"),
            RuneLanguage.TypeScript =>
                ("scriptc", "typescript"),
            RuneLanguage.Python =>
                ("python", "python"),
            RuneLanguage.Rust =>
                ("rust", "rust"),
            RuneLanguage.C =>
                ("clang", "c"),
            RuneLanguage.Cpp =>
                ("clang", "cpp"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(language),
                language,
                "Rune language has no Firecracker build target.")
        };

    public async ValueTask<BuiltRuneArtifact> BuildAsync(
        RuneLanguage language,
        string source,
        CancellationToken cancellationToken = default)
    {
        var (pool, wireLanguage) =
            GetBuildTarget(language);

        var sourcePath =
            Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(
                sourcePath,
                source,
                cancellationToken);

            var start =
                new ProcessStartInfo("bash")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };

            start.ArgumentList.Add(scriptPath);
            start.ArgumentList.Add(pool);
            start.ArgumentList.Add(wireLanguage);
            start.ArgumentList.Add(sourcePath);

            using var process =
                Process.Start(start)
                ?? throw new RuneCompilationException(
                    "Failed to start the Rune build VM.");

            var stdout =
                process.StandardOutput.ReadToEndAsync(
                    cancellationToken);
            var stderr =
                process.StandardError.ReadToEndAsync(
                    cancellationToken);

            await process.WaitForExitAsync(
                cancellationToken);

            var error =
                (await stderr).Trim();

            if (process.ExitCode != 0)
            {
                throw new RuneCompilationException(
                    string.IsNullOrWhiteSpace(error)
                        ? "Rune compilation failed."
                        : error);
            }

            var fields =
                (await stdout)
                    .Split(
                        (char[]?)null,
                        StringSplitOptions.RemoveEmptyEntries);

            if (fields.Length != 3 ||
                !ulong.TryParse(
                    fields[1],
                    out var size))
            {
                throw new RuneCompilationException(
                    "Rune build VM returned an invalid artifact descriptor.");
            }

            return new BuiltRuneArtifact(
                fields[0],
                fields[0],
                fields[2],
                size);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }
}
