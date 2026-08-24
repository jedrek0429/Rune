using Rune.Core.Runes;

namespace Rune.Runtime.Compilation;

public sealed class WasmPipeline
{
    public async ValueTask<CompiledRune> ProcessAsync(
        string wasmPath,
        IReadOnlyList<string>? diagnostics = null,
        CancellationToken cancellationToken = default)
    {
        var wasm = await File.ReadAllBytesAsync(
            wasmPath,
            cancellationToken);

        return new CompiledRune(
            wasm,
            diagnostics ?? []);
    }
}
