using System.Diagnostics;
using Rune.Core.Runes;

namespace Rune.Runtime;

public sealed class FirecrackerRuneBuilder(
    string scriptPath = "firecracker/run-build-vm.sh") : IRuneBuilder
{
    public static (string Pool, string Language) GetBuildTarget(RuneLanguage language) =>
        language switch
        {
            RuneLanguage.JavaScript => ("scriptc", "javascript"),
            RuneLanguage.TypeScript => ("scriptc", "typescript"),
            RuneLanguage.Rust => ("rust", "rust"),
            RuneLanguage.C => ("clang", "c"),
            RuneLanguage.Cpp => ("clang", "cpp"),
            RuneLanguage.CSharp => ("dotnet-aot", "csharp"),
            RuneLanguage.Python => ("python", "python"),
            RuneLanguage.Ruby => ("ruby", "ruby"),
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };

    public async ValueTask<BuiltRuneArtifact> BuildAsync(
        RuneLanguage language,
        string source,
        CancellationToken cancellationToken = default)
    {
        var (pool, wireLanguage) = GetBuildTarget(language);
        var sourcePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(sourcePath, source, cancellationToken);
            var start = new ProcessStartInfo("bash")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            start.ArgumentList.Add(scriptPath);
            start.ArgumentList.Add(pool);
            start.ArgumentList.Add(wireLanguage);
            start.ArgumentList.Add(sourcePath);

            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Failed to start Rune build VM.");
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
                throw new InvalidOperationException((await stderr).Trim());

            var fields = (await stdout)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 3 || !long.TryParse(fields[1], out var size))
                throw new InvalidOperationException("Rune build VM returned an invalid artifact descriptor.");

            return new BuiltRuneArtifact(fields[0], fields[0], fields[2], size);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }
}
