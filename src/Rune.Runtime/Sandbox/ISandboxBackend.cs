using Rune.Core.Runes;

namespace Rune.Runtime.Sandbox;

public interface ISandboxBackend
{
    ValueTask<ISandbox> StartAsync(
        RegisteredRune rune,
        CancellationToken cancellationToken = default);
}
