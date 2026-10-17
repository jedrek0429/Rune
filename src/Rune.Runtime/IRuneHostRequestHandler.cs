namespace Rune.Runtime;

public interface IRuneHostRequestHandler
{
    ValueTask HandleAsync(
        RuneHostRequest request,
        CancellationToken cancellationToken = default);
}
