using System.Collections.Concurrent;

using Rune.Core.Runes;
using Rune.Runtime.Exceptions;

namespace Rune.Runtime.Sandbox;

public sealed class SandboxManager(
    ISandboxBackend backend) : IAsyncDisposable
{
    private sealed class Entry
    {
        public ISandbox? Sandbox { get; set; }

        public SemaphoreSlim Gate { get; } = new(1, 1);
    }

    private readonly ConcurrentDictionary<Guid, Entry> _sandboxes = [];

    private int _disposed;

    public async ValueTask UseAsync(
        RegisteredRune rune,
        Func<ISandbox, CancellationToken, ValueTask> action,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);

        var entry = _sandboxes.GetOrAdd(
            rune.Id,
            static _ => new Entry());

        await entry.Gate.WaitAsync(cancellationToken);

        try
        {
            if (entry.Sandbox is null || !entry.Sandbox.IsAlive)
            {
                await DisposeSandboxAsync(entry);

                entry.Sandbox = await backend.StartAsync(
                    rune,
                    cancellationToken);
            }

            try
            {
                await action(
                    entry.Sandbox,
                    cancellationToken);
            }
            catch (SandboxCommunicationException)
            {
                // The invocation may already have started, so do not replay it.
                // Just invalidate the sandbox. The next invocation gets a new one.
                await DisposeSandboxAsync(entry);
            }
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public async ValueTask StopAsync(
        Guid runeId,
        CancellationToken cancellationToken = default)
    {
        if (!_sandboxes.TryGetValue(runeId, out var entry))
            return;

        await entry.Gate.WaitAsync(cancellationToken);

        try
        {
            await DisposeSandboxAsync(entry);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (var entry in _sandboxes.Values)
        {
            await entry.Gate.WaitAsync();

            try
            {
                await DisposeSandboxAsync(entry);
            }
            finally
            {
                entry.Gate.Release();
            }
        }

        _sandboxes.Clear();
    }

    private static async ValueTask DisposeSandboxAsync(
        Entry entry)
    {
        if (entry.Sandbox is null)
            return;

        await entry.Sandbox.DisposeAsync();
        entry.Sandbox = null;
    }
}
