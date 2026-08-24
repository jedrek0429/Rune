using Rune.Runtime.Native;
using Xunit;

namespace Rune.Runtime.Native.IntegrationTests;

public sealed class NativeRuntimeContractTests
{
    [Fact]
    public void Native_runtime_exposes_supported_abi_version()
    {
        Assert.Equal(1U, RuneNativeRuntime.AbiVersion);
    }

    [Fact]
    public void Component_reply_crosses_the_native_boundary()
    {
        var componentPath = Path.Combine(
            AppContext.BaseDirectory,
            "message_create_component.wasm");

        using var runtime = new RuneNativeRuntime();
        runtime.LoadComponent(File.ReadAllBytes(componentPath));

        var reply = runtime.InvokeMessageCreate("Ada");

        Assert.Equal("Hello, Ada!", reply);
    }
}
