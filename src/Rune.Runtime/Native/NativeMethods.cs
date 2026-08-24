using System.Runtime.InteropServices;

namespace Rune.Runtime.Native;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeBuffer
{
    internal readonly nint Data;
    internal readonly nuint Length;
}

internal static partial class NativeMethods
{
    private const string LibraryName = "rune_runtime";

    [LibraryImport(
        LibraryName,
        EntryPoint = "rune_runtime_abi_version")]
    internal static partial uint GetAbiVersion();

    [LibraryImport(
        LibraryName,
        EntryPoint = "rune_runtime_create")]
    internal static partial nint Create();

    [LibraryImport(
        LibraryName,
        EntryPoint = "rune_runtime_load_component")]
    internal static unsafe partial int LoadComponent(
        nint runtime,
        byte* componentData,
        nuint componentLength);

    [LibraryImport(
        LibraryName,
        EntryPoint = "rune_runtime_invoke_message_create")]
    internal static unsafe partial int InvokeMessageCreate(
        nint runtime,
        byte* authorData,
        nuint authorLength,
        out NativeBuffer reply);

    [LibraryImport(
        LibraryName,
        EntryPoint = "rune_runtime_buffer_free")]
    internal static partial void FreeBuffer(NativeBuffer buffer);

    [LibraryImport(
        LibraryName,
        EntryPoint = "rune_runtime_destroy")]
    internal static partial void Destroy(nint runtime);
}
