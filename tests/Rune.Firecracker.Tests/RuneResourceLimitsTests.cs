using Rune.Core.Runes;

namespace Rune.Firecracker.Tests;

public sealed class RuneResourceLimitsTests
{
    [Theory]
    [InlineData(RuneLanguage.JavaScript, 512, 30)]
    [InlineData(RuneLanguage.TypeScript, 512, 30)]
    [InlineData(RuneLanguage.C, 512, 20)]
    [InlineData(RuneLanguage.Cpp, 512, 20)]
    [InlineData(RuneLanguage.Rust, 1024, 45)]
    [InlineData(RuneLanguage.CSharp, 2048, 60)]
    [InlineData(RuneLanguage.Python, 512, 20)]
    [InlineData(RuneLanguage.Ruby, 512, 20)]
    public void BuildLimitsAreCompilerSpecific(
        RuneLanguage language,
        int expectedMemoryMiB,
        int expectedSeconds)
    {
        var limits = RuneResourceLimits.BuildFor(language);

        Assert.Equal(expectedMemoryMiB, limits.MemoryMiB);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), limits.WallTime);
        Assert.InRange(limits.VcpuCount, 1, 2);
        Assert.Equal(RuneResourceLimits.MaxArtifactBytes, limits.MaxArtifactBytes);
        Assert.True(limits.PidLimit > 0);
        Assert.True(limits.FileDescriptorLimit > 0);
        Assert.True(limits.WritableDiskMiB > 0);
    }

    [Theory]
    [InlineData(RuneLanguage.JavaScript, 192)]
    [InlineData(RuneLanguage.TypeScript, 192)]
    [InlineData(RuneLanguage.Rust, 192)]
    [InlineData(RuneLanguage.C, 192)]
    [InlineData(RuneLanguage.Cpp, 192)]
    [InlineData(RuneLanguage.CSharp, 192)]
    [InlineData(RuneLanguage.Python, 256)]
    [InlineData(RuneLanguage.Ruby, 256)]
    public void InvocationLimitsAreRuntimeSpecific(RuneLanguage language, int expectedMemoryMiB)
    {
        var limits = RuneResourceLimits.InvocationFor(language);

        Assert.Equal(expectedMemoryMiB, limits.MemoryMiB);
        Assert.Equal(TimeSpan.FromSeconds(3), limits.WallTime);
        Assert.Equal(1, limits.VcpuCount);
        Assert.Equal(1, limits.MaxConcurrentPerRune);
        Assert.Equal(4, limits.MaxConcurrentPerGuild);
        Assert.Equal(RuneResourceLimits.MaxArtifactBytes, limits.MaxArtifactBytes);
    }

    [Fact]
    public void SourceAndArtifactLimitsAreFinite()
    {
        Assert.Equal(64 * 1024, RuneResourceLimits.MaxSourceBytes);
        Assert.Equal(16 * 1024 * 1024, RuneResourceLimits.MaxArtifactBytes);
    }
}
