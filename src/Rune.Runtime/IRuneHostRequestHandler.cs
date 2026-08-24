using Rune.Core.Invocations;

namespace Rune.Runtime;

public interface IRuneHostRequestHandler
{
    ValueTask HandleAsync(
        EventRuneInvocation invocation,
        RuneHostRequest request,
        CancellationToken cancellationToken = default);
}
