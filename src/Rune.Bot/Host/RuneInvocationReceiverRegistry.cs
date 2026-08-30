using System.Collections.Concurrent;

namespace Rune.Bot.Host;

public sealed class RuneInvocationReceiverRegistry
{
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

    public IDisposable Register(Guid invocationId, object receiver)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        var entry = new Entry(receiver);

        if (!_entries.TryAdd(invocationId, entry))
        {
            throw new InvalidOperationException(
                $"Invocation {invocationId} already has a registered receiver.");
        }

        return new Lease(this, invocationId, entry);
    }

    public T GetRequired<T>(Guid invocationId)
        where T : class
    {
        if (!_entries.TryGetValue(invocationId, out var entry))
        {
            throw new InvalidOperationException(
                $"Invocation {invocationId} has no registered receiver.");
        }

        if (entry.Receiver is not T typed)
        {
            throw new InvalidOperationException(
                $"Invocation {invocationId} receiver is not a {typeof(T).FullName}.");
        }

        return typed;
    }

    public void Seal(Guid invocationId, int expectedExecutions)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedExecutions);
        var entry = GetEntry(invocationId);
        var remove = false;

        lock (entry.Gate)
        {
            if (entry.Sealed)
            {
                throw new InvalidOperationException(
                    $"Invocation {invocationId} receiver is already sealed.");
            }

            entry.Sealed = true;
            entry.ExpectedExecutions = expectedExecutions;
            remove = entry.CompletedExecutions >= expectedExecutions;
        }

        if (remove)
            Remove(invocationId, entry);
    }

    public void CompleteExecution(Guid invocationId)
    {
        var entry = GetEntry(invocationId);
        var remove = false;

        lock (entry.Gate)
        {
            entry.CompletedExecutions += 1;
            remove = entry.Sealed &&
                entry.CompletedExecutions >= entry.ExpectedExecutions;
        }

        if (remove)
            Remove(invocationId, entry);
    }

    private Entry GetEntry(Guid invocationId)
    {
        if (_entries.TryGetValue(invocationId, out var entry))
            return entry;

        throw new InvalidOperationException(
            $"Invocation {invocationId} has no registered receiver.");
    }

    private void Remove(Guid invocationId, Entry entry)
    {
        _entries.TryRemove(
            new KeyValuePair<Guid, Entry>(invocationId, entry));
    }

    private sealed class Entry(object receiver)
    {
        public object Gate { get; } = new();
        public object Receiver { get; } = receiver;
        public bool Sealed { get; set; }
        public int ExpectedExecutions { get; set; }
        public int CompletedExecutions { get; set; }
    }

    private sealed class Lease(
        RuneInvocationReceiverRegistry registry,
        Guid invocationId,
        Entry entry)
        : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            registry.Remove(invocationId, entry);
        }
    }
}
