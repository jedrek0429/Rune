using System.Collections.Concurrent;

namespace Rune.Bot.Host;

public sealed class RuneInvocationReceiverRegistry
{
    private readonly ConcurrentDictionary<Guid, object> _receivers = new();

    public IDisposable Register(Guid invocationId, object receiver)
    {
        ArgumentNullException.ThrowIfNull(receiver);

        if (!_receivers.TryAdd(invocationId, receiver))
        {
            throw new InvalidOperationException(
                $"Invocation {invocationId} already has a registered receiver.");
        }

        return new Lease(this, invocationId, receiver);
    }

    public T GetRequired<T>(Guid invocationId)
        where T : class
    {
        if (!_receivers.TryGetValue(invocationId, out var receiver))
        {
            throw new InvalidOperationException(
                $"Invocation {invocationId} has no registered receiver.");
        }

        if (receiver is not T typed)
        {
            throw new InvalidOperationException(
                $"Invocation {invocationId} receiver is not a {typeof(T).FullName}.");
        }

        return typed;
    }

    private void Remove(Guid invocationId, object receiver)
    {
        _receivers.TryRemove(
            new KeyValuePair<Guid, object>(invocationId, receiver));
    }

    private sealed class Lease(
        RuneInvocationReceiverRegistry registry,
        Guid invocationId,
        object receiver)
        : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            registry.Remove(invocationId, receiver);
        }
    }
}
