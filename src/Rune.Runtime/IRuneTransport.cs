using Rune.Core.Invocations;

namespace Rune.Runtime;

public interface IRuneTransport
{
    ValueTask EnqueueAsync(
        RuneInvocationEnvelope envelope,
        CancellationToken cancellationToken = default);
}
