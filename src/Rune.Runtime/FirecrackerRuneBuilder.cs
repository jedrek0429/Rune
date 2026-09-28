using System.Diagnostics;

using Rune.Api;

using Rune.Runtime.Exceptions;

namespace Rune.Runtime;

public sealed class FirecrackerRuneBuilder(
    string scriptPath = "src/Rune.Firecracker/run-build-vm.sh",
    string? repositoryRoot = null)
    : IRuneBuilder
{
    private readonly string _repositoryRoot =
        repositoryRoot ??
        FindRepositoryRoot();


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
        RuneApiEventType eventType,
        string source,
        CancellationToken cancellationToken = default)
    {
        var (pool, wireLanguage) =
            GetBuildTarget(language);

        source =
            RuneSourceComposer.Compose(
                language,
                eventType,
                source,
                ReadOptional(
                    "generated/typescript/rune-api.ts"),
                ReadOptional(
                    "generated/javascript/rune-api.js"),
                ReadOptional(
                    "generated/rust/rune_api.rs"));

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
    private string ReadOptional(
        params string[] relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            var path =
                Path.Combine(
                    _repositoryRoot,
                    relativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar));

            if (File.Exists(path))
                return File.ReadAllText(path);
        }

        return string.Empty;
    }

    private static string FindRepositoryRoot()
    {
        var directory =
            new DirectoryInfo(
                Directory.GetCurrentDirectory());

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "Rune.slnx")))
            {
                return directory.FullName;
            }

            directory =
                directory.Parent;
        }

        throw new InvalidOperationException(
            "Rune repository root was not found.");
    }

}
