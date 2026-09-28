using Rune.Core.Invocations;
using Rune.Core.Runes;

namespace Rune.Runtime;

public sealed class RuneEventDispatcher(
    RuneRegistry registry,
    IRuneTransport transport)
{
    public async ValueTask<RuneDispatchResult>
        DispatchAsync(
            EventRuneInvocation invocation,
            CancellationToken cancellationToken = default)
    {
        var failures =
            new List<RuneFailure>();

        var queuedExecutions =
            0;

        var runes =
            registry.GetEventRunes(
                invocation.GuildId,
                invocation.EventType);

        var payload =
            RuneEventCodec.ToPayload(
                invocation);

        foreach (var rune in runes)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            if (rune.Artifact is null)
            {
                failures.Add(
                    new RuneFailure(
                        rune.Name,
                        "The rune has not been built yet."));

                continue;
            }

            if (rune.Artifact.SizeBytes <= 0 ||
                rune.Artifact.SizeBytes >
                    16 * 1024 * 1024)
            {
                failures.Add(
                    new RuneFailure(
                        rune.Name,
                        "The built rune artifact is invalid."));

                continue;
            }

            try
            {
                await transport.EnqueueAsync(
                    new RuneInvocationEnvelope(
                        Guid.NewGuid(),
                        invocation.InvocationId,
                        rune.Id,
                        rune.Name,
                        invocation.GuildId,
                        rune.EventType,
                        rune.Artifact,
                        payload,
                        DateTimeOffset.UtcNow),
                    cancellationToken);

                queuedExecutions += 1;
            }
            catch (Exception exception)
                when (
                    exception is not
                        OperationCanceledException)
            {
                failures.Add(
                    new RuneFailure(
                        rune.Name,
                        "The invocation could not be queued."));
            }
        }

        return new RuneDispatchResult(
            failures,
            queuedExecutions);
    }
}

public sealed record RuneDispatchResult(
    IReadOnlyList<RuneFailure> Failures,
    int QueuedExecutions);

public sealed record RuneFailure(
    string RuneName,
    string Message);
