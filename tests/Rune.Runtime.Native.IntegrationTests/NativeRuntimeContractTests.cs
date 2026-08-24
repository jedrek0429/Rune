using Rune.Runtime.Native;

namespace Rune.Runtime.Native.IntegrationTests;

public sealed class NativeRuntimeContractTests
{
    [Fact]
    public void Native_runtime_exposes_supported_abi_version()
    {
        Assert.Equal(1U, RuneNativeRuntime.AbiVersion);
    }
}
