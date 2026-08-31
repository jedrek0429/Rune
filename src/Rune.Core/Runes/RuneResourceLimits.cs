namespace Rune.Core.Runes;

public sealed record RuneBuildLimits(
    int VcpuCount,
    int MemoryMiB,
    TimeSpan WallTime,
    int PidLimit,
    int FileDescriptorLimit,
    int WritableDiskMiB,
    int MaxDiagnosticsBytes,
    int MaxArtifactBytes);

public sealed record RuneInvocationLimits(
    int VcpuCount,
    int MemoryMiB,
    TimeSpan WallTime,
    int PidLimit,
    int FileDescriptorLimit,
    int WritableDiskMiB,
    int MaxResponseBytes,
    int MaxArtifactBytes,
    int MaxConcurrentPerRune,
    int MaxConcurrentPerGuild);

public static class RuneResourceLimits
{
    public const int MaxSourceBytes = 64 * 1024;
    public const int MaxArtifactBytes = 16 * 1024 * 1024;

    public static RuneBuildLimits BuildFor(RuneLanguage language) => language switch
    {
        RuneLanguage.JavaScript or RuneLanguage.TypeScript =>
            new(1, 512, TimeSpan.FromSeconds(30), 128, 256, 512, 64 * 1024, MaxArtifactBytes),
        RuneLanguage.C or RuneLanguage.Cpp =>
            new(1, 512, TimeSpan.FromSeconds(20), 128, 256, 512, 64 * 1024, MaxArtifactBytes),
        RuneLanguage.Rust =>
            new(2, 1024, TimeSpan.FromSeconds(45), 128, 256, 512, 64 * 1024, MaxArtifactBytes),
        RuneLanguage.CSharp =>
            new(2, 2048, TimeSpan.FromSeconds(60), 128, 256, 768, 64 * 1024, MaxArtifactBytes),
        RuneLanguage.Python or RuneLanguage.Ruby =>
            new(1, 512, TimeSpan.FromSeconds(20), 128, 256, 256, 64 * 1024, MaxArtifactBytes),
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
    };

    public static RuneInvocationLimits InvocationFor(RuneLanguage language) => language switch
    {
        RuneLanguage.JavaScript or RuneLanguage.TypeScript or RuneLanguage.Rust or
        RuneLanguage.C or RuneLanguage.Cpp or RuneLanguage.CSharp =>
            new(1, 192, TimeSpan.FromSeconds(3), 32, 128, 32, 256 * 1024,
                MaxArtifactBytes, 1, 4),
        RuneLanguage.Python or RuneLanguage.Ruby =>
            new(1, 256, TimeSpan.FromSeconds(3), 32, 128, 32, 256 * 1024,
                MaxArtifactBytes, 1, 4),
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
    };
}
