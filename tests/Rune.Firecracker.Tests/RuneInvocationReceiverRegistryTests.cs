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
}
