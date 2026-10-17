using Rune.Runtime;

using Xunit;

namespace Rune.Runtime.Tests;

public sealed class RuneInvocationReceiverRegistryTests
{
    [Fact]
    public void Registered_receiver_is_returned_by_identity()
    {
        var registry =
            new RuneInvocationReceiverRegistry();

        var invocationId =
            Guid.NewGuid();

        var receiver =
            new object();

        registry.Register(
            invocationId,
            receiver);

        Assert.Same(
            receiver,
            registry.GetRequired<object>(
                invocationId));
    }

    [Fact]
    public void Duplicate_registration_is_rejected()
    {
        var registry =
            new RuneInvocationReceiverRegistry();

        var invocationId =
            Guid.NewGuid();

        registry.Register(
            invocationId,
            new object());

        Assert.Throws<
            InvalidOperationException>(
                () =>
                    registry.Register(
                        invocationId,
                        new object()));
    }

    [Fact]
    public void Sealing_zero_executions_removes_receiver()
    {
        var registry =
            new RuneInvocationReceiverRegistry();

        var invocationId =
            Guid.NewGuid();

        registry.Register(
            invocationId,
            new object());

        registry.Seal(
            invocationId,
            0);

        Assert.Throws<
            InvalidOperationException>(
                () =>
                    registry.GetRequired<object>(
                        invocationId));
    }

    [Fact]
    public void Completion_before_seal_is_retained_until_expected_count_is_known()
    {
        var registry =
            new RuneInvocationReceiverRegistry();

        var invocationId =
            Guid.NewGuid();

        var receiver =
            new object();

        registry.Register(
            invocationId,
            receiver);

        Assert.True(
            registry.CompleteExecution(
                invocationId));

        Assert.Same(
            receiver,
            registry.GetRequired<object>(
                invocationId));

        registry.Seal(
            invocationId,
            1);

        Assert.Throws<
            InvalidOperationException>(
                () =>
                    registry.GetRequired<object>(
                        invocationId));
    }

    [Fact]
    public void Receiver_lives_until_every_queued_execution_completes()
    {
        var registry =
            new RuneInvocationReceiverRegistry();

        var invocationId =
            Guid.NewGuid();

        var receiver =
            new object();

        registry.Register(
            invocationId,
            receiver);

        registry.Seal(
            invocationId,
            2);

        Assert.True(
            registry.CompleteExecution(
                invocationId));

        Assert.Same(
            receiver,
            registry.GetRequired<object>(
                invocationId));

        Assert.True(
            registry.CompleteExecution(
                invocationId));

        Assert.Throws<
            InvalidOperationException>(
                () =>
                    registry.GetRequired<object>(
                        invocationId));
    }

    [Fact]
    public void Completion_for_unknown_invocation_is_harmless()
    {
        var registry =
            new RuneInvocationReceiverRegistry();

        Assert.False(
            registry.CompleteExecution(
                Guid.NewGuid()));
    }
}
