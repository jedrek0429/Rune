namespace Rune.Runtime;

public interface IRuneTransport
{
    ValueTask EnqueueAsync(
        RuneInvocationEnvelope envelope,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<RuneResultMessage>> ReadResultsAsync(
        string consumerName,
        CancellationToken cancellationToken = default);

    ValueTask AcknowledgeResultAsync(
        string streamId,
        CancellationToken cancellationToken = default);
}
