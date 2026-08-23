using Rune.Core.Invocations;
using Rune.Runtime.Protocol;

namespace Rune.Runtime;

public interface IRuneHostRequestHandler
{
    ValueTask HandleAsync(
        EventRuneInvocation invocation,
        RuneHostRequest request,
        CancellationToken cancellationToken = default);
}
