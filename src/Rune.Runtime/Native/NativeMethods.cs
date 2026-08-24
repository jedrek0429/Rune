using System.Runtime.InteropServices;

namespace Rune.Runtime.Native;

internal static partial class NativeMethods
{
    [LibraryImport(
        "rune_runtime",
        EntryPoint = "rune_runtime_abi_version")]
    internal static partial uint GetAbiVersion();
}
