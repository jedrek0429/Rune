using Rune.Bot.Host;

namespace Rune.Firecracker.Tests;

public sealed class RuneInvocationReceiverRegistryTests
{
    [Fact]
    public void RegistryReturnsTheExactReceiverRegisteredForAnInvocation()
    {
        var registry = new RuneInvocationReceiverRegistry();
        var invocationId = Guid.NewGuid();
        var receiver = new object();

        using var lease = registry.Register(invocationId, receiver);

        Assert.Same(receiver, registry.GetRequired<object>(invocationId));
    }

    [Fact]
    public void DisposingTheLeaseRemovesTheReceiver()
    {
        var registry = new RuneInvocationReceiverRegistry();
        var invocationId = Guid.NewGuid();
        var lease = registry.Register(invocationId, new object());

        lease.Dispose();

        var exception = Assert.Throws<InvalidOperationException>(
            () => registry.GetRequired<object>(invocationId));
        Assert.Contains(invocationId.ToString(), exception.Message);
    }

    [Fact]
    public void RegisteringTheSameInvocationTwiceIsRejected()
    {
        var registry = new RuneInvocationReceiverRegistry();
        var invocationId = Guid.NewGuid();

        using var lease = registry.Register(invocationId, new object());

        Assert.Throws<InvalidOperationException>(
            () => registry.Register(invocationId, new object()));
    }

    [Fact]
    public void ReceiverStaysAliveUntilEveryQueuedExecutionCompletes()
    {
        var registry = new RuneInvocationReceiverRegistry();
        var invocationId = Guid.NewGuid();
        var receiver = new object();
        using var lease = registry.Register(invocationId, receiver);

        registry.Seal(invocationId, expectedExecutions: 2);
        registry.CompleteExecution(invocationId);

        Assert.Same(receiver, registry.GetRequired<object>(invocationId));

        registry.CompleteExecution(invocationId);

        Assert.Throws<InvalidOperationException>(
            () => registry.GetRequired<object>(invocationId));
    }

    [Fact]
    public void CompletionBeforeSealIsAccountedForWithoutDroppingTheReceiverEarly()
    {
        var registry = new RuneInvocationReceiverRegistry();
        var invocationId = Guid.NewGuid();
        var receiver = new object();
        using var lease = registry.Register(invocationId, receiver);

        registry.CompleteExecution(invocationId);
        Assert.Same(receiver, registry.GetRequired<object>(invocationId));

        registry.Seal(invocationId, expectedExecutions: 1);

        Assert.Throws<InvalidOperationException>(
            () => registry.GetRequired<object>(invocationId));
    }

    [Fact]
    public void SealingAnInvocationWithNoQueuedExecutionsRemovesItImmediately()
    {
        var registry = new RuneInvocationReceiverRegistry();
        var invocationId = Guid.NewGuid();
        using var lease = registry.Register(invocationId, new object());

        registry.Seal(invocationId, expectedExecutions: 0);

        Assert.Throws<InvalidOperationException>(
            () => registry.GetRequired<object>(invocationId));
    }
}
