namespace Rune.Runtime.Sandbox;

public interface ISandbox : IAsyncDisposable
{
    bool IsAlive { get; }

    ValueTask SendAsync(
        string message,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> ReadAsync(
        CancellationToken cancellationToken = default);

    ValueTask KillAsync(
        CancellationToken cancellationToken = default);
}
