using System.Collections.Concurrent;

using Extism.Sdk;

using Rune.Core.Runes;
using Rune.Runtime.Exceptions;

namespace Rune.Runtime.Wasm;

public sealed class RuneExecutor(
    RuneWasmCache cache,
    RuneRuntimeOptions options)
    : IAsyncDisposable
{
    private readonly SemaphoreSlim _concurrency =
        new(
            options.MaxConcurrentExecutions,
            options.MaxConcurrentExecutions);

    private readonly ConcurrentDictionary<
        Guid,
        ConcurrentDictionary<Guid, ActiveExecution>>
        _active = [];

    // StopAsync makes the stop atomic with respect to
    // executions that were already dispatched.
    private readonly ConcurrentDictionary<Guid, byte>
        _stopped = [];

    private int _disposed;

    public async ValueTask<RuneExecutionResult> ExecuteAsync(
        RegisteredRune rune,
        Guid invocationId,
        byte[] input,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);

        ThrowIfStopped(rune.Id);

        var executionId =
            Guid.NewGuid();

        var execution =
            new ActiveExecution();

        var active =
            _active.GetOrAdd(
                rune.Id,
                static _ => []);

        active[executionId] =
            execution;

        try
        {
            // Close the race where StopAsync happened
            // between our first check and registration.
            ThrowIfStopped(rune.Id);

            using var lifetime =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken,
                        execution.Stop.Token);

            await _concurrency.WaitAsync(
                lifetime.Token);

            try
            {
                ThrowIfStopped(rune.Id);

                using var plugin =
                    cache.Instantiate(rune);

                var context =
                    new RuneExecutionContext(
                        invocationId,
                        options);

                using var deadline =
                    CancellationTokenSource
                        .CreateLinkedTokenSource(
                            lifetime.Token);

                deadline.CancelAfter(
                    options.ExecutionTimeout);

                try
                {
                    await Task.Run(
                        () =>
                        {
                            _ = plugin.CallWithHostContext(
                                "handle",
                                input,
                                context,
                                deadline.Token);
                        },
                        CancellationToken.None);
                }
                catch (ExtismException)
                    when (deadline.IsCancellationRequested)
                {
                    if (lifetime.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(
                            lifetime.Token);
                    }

                    throw new RuneTimeoutException(
                        options.ExecutionTimeout);
                }
                catch (ExtismException exception)
                {
                    throw new RuneExecutionException(
                        UserFacingError(
                            exception.Message),
                        exception);
                }

                lifetime.Token
                    .ThrowIfCancellationRequested();

                if (context.Error is not null)
                {
                    throw new RuneExecutionException(
                        context.Error);
                }

                return new RuneExecutionResult(
                    context.Requests.ToArray());
            }
            finally
            {
                _concurrency.Release();
            }
        }
        finally
        {
            active.TryRemove(
                executionId,
                out _);

            if (active.IsEmpty)
            {
                _active.TryRemove(
                    rune.Id,
                    out _);
            }

            execution.Complete();
            execution.Dispose();
        }
    }

    public async ValueTask StopAsync(
        Guid runeId)
    {
        // Prevent anything already dispatched but not yet
        // executing from starting.
        _stopped[runeId] = 0;

        if (_active.TryGetValue(
                runeId,
                out var active))
        {
            var executions =
                active.Values.ToArray();

            foreach (var execution in executions)
                execution.StopExecution();

            await Task.WhenAll(
                executions.Select(
                    execution =>
                        execution.Completion));
        }

        // Safe now: no instances belonging to this
        // CompiledPlugin remain active.
        cache.Invalidate(runeId);
    }

    public void Resume(
        Guid runeId)
    {
        _stopped.TryRemove(
            runeId,
            out _);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(
                ref _disposed,
                1) != 0)
        {
            return;
        }

        var executions =
            _active.Values
                .SelectMany(
                    group => group.Values)
                .Distinct()
                .ToArray();

        foreach (var execution in executions)
            execution.StopExecution();

        await Task.WhenAll(
            executions.Select(
                execution =>
                    execution.Completion));

        _concurrency.Dispose();
    }

    private void ThrowIfStopped(
        Guid runeId)
    {
        if (_stopped.ContainsKey(runeId))
            throw new OperationCanceledException(
                "Rune is stopped.");
    }

    private static string UserFacingError(
        string message)
    {
        var line = message
            .Replace("\r\n", "\n")
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim();

        if (string.IsNullOrWhiteSpace(line))
            return "Rune execution failed.";

        return line.Length <= 500
            ? line
            : line[..500] + "…";
    }

    private sealed class ActiveExecution
        : IDisposable
    {
        public CancellationTokenSource Stop { get; } =
            new();

        private readonly TaskCompletionSource<bool>
            _completion =
                new(
                    TaskCreationOptions
                        .RunContinuationsAsynchronously);

        public Task Completion =>
            _completion.Task;

        public void StopExecution()
        {
            try
            {
                Stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Complete()
        {
            _completion.TrySetResult(true);
        }

        public void Dispose()
        {
            Stop.Dispose();
        }
    }
}
