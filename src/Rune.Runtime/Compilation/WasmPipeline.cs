using Rune.Core.Runes;
using Rune.Core.Api.Generated;

namespace Rune.Runtime.Compilation;

public sealed class WasmPipeline
{
    public async ValueTask<CompiledRune> ProcessAsync(
        string wasmPath,
        RuneEventType eventType,
        IReadOnlyList<string>? diagnostics = null,
        CancellationToken cancellationToken = default)
    {
        var wasm = await File.ReadAllBytesAsync(
            wasmPath,
            cancellationToken);

        return new CompiledRune(
            wasm,
            diagnostics ?? [],
            eventType,
            RuneApiMetadata.Fingerprint);
    }
}
