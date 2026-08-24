using Rune.Runtime.Native;
using Xunit;

namespace Rune.Runtime.Native.IntegrationTests;

public sealed class NativeRuntimeContractTests
{
    [Fact]
    public void Native_runtime_exposes_supported_abi_version()
    {
        Assert.Equal(2U, RuneNativeRuntime.AbiVersion);
    }

    [Fact]
    public void Component_actions_cross_the_native_boundary_in_order()
    {
        var componentPath = Path.Combine(
            AppContext.BaseDirectory,
            "message_create_component.wasm");

        using var runtime = new RuneNativeRuntime();
        runtime.LoadComponent(File.ReadAllBytes(componentPath));

        var result = runtime.InvokeMessageCreate("Ada");

        Assert.Collection(
            result.Actions,
            action =>
            {
                Assert.Equal(RuneActionKind.Reply, action.Kind);
                Assert.Equal("Hello, Ada!", action.Content);
            },
            action =>
            {
                Assert.Equal(RuneActionKind.Reply, action.Kind);
                Assert.Equal("Welcome to Rune.", action.Content);
            });
    }

    [Fact]
    public void Failed_component_discards_actions_and_reports_native_detail()
    {
        var componentPath = Path.Combine(
            AppContext.BaseDirectory,
            "message_create_component.wasm");

        using var runtime = new RuneNativeRuntime();
        runtime.LoadComponent(File.ReadAllBytes(componentPath));

        var exception = Assert.Throws<RuneNativeException>(
            () => runtime.InvokeMessageCreate("trap"));

        Assert.Equal(2, exception.NativeStatus);
        Assert.Contains(
            "wasm",
            exception.NativeDetail,
            StringComparison.OrdinalIgnoreCase);

        var recovered = runtime.InvokeMessageCreate("Ada");

        Assert.DoesNotContain(
            recovered.Actions,
            action => action.Content == "discard me");
        Assert.Equal(2, recovered.Actions.Count);
    }
}
