using System.Text.Json;
using Rune.Core.Invocations;
using Rune.Core.Runes;

namespace Rune.Runtime;

public sealed class RuneEventDispatcher(
    RuneRegistry registry,
    IRuneTransport transport)
{
    public async ValueTask<IReadOnlyList<RuneFailure>> DispatchAsync(
        EventRuneInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<RuneFailure>();
        var runes = registry.GetEventRunes(invocation.GuildId, invocation.EventType);
        var payload = RuneEventCodec.ToPayload(invocation);

        foreach (var rune in runes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await transport.EnqueueAsync(
                    new RuneInvocationEnvelope(
                        Guid.NewGuid(),
                        invocation.InvocationId,
                        rune.Id,
                        rune.Name,
                        invocation.GuildId,
                        rune.Language,
                        rune.EventType,
                        rune.Source,
                        payload,
                        DateTimeOffset.UtcNow),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(new RuneFailure(rune.Name, "The invocation could not be queued."));
            }
        }

        return failures;
    }

    internal static byte[] Serialize(EventRuneInvocation invocation) =>
        JsonSerializer.SerializeToUtf8Bytes(RuneEventCodec.ToPayload(invocation));
}

public sealed record RuneFailure(
    string RuneName,
    string Message);
