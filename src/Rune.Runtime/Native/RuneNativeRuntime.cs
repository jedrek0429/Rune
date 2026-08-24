using System.Runtime.InteropServices;
using System.Text;

namespace Rune.Runtime.Native;

public sealed class RuneNativeRuntime : IDisposable
{
    private nint handle;

    public RuneNativeRuntime()
    {
        handle = NativeMethods.Create();
        if (handle == nint.Zero)
        {
            throw new RuneNativeException("The native runtime could not be created.");
        }
    }

    public static uint AbiVersion => NativeMethods.GetAbiVersion();

    public unsafe void LoadComponent(ReadOnlySpan<byte> component)
    {
        ObjectDisposedException.ThrowIf(handle == nint.Zero, this);

        fixed (byte* componentData = component)
        {
            ThrowIfFailed(
                NativeMethods.LoadComponent(
                    handle,
                    componentData,
                    (nuint)component.Length),
                "The component could not be loaded.");
        }
    }

    public unsafe string InvokeMessageCreate(string authorUsername)
    {
        ArgumentNullException.ThrowIfNull(authorUsername);
        ObjectDisposedException.ThrowIf(handle == nint.Zero, this);

        var author = Encoding.UTF8.GetBytes(authorUsername);
        NativeBuffer reply = default;

        fixed (byte* authorData = author)
        {
            ThrowIfFailed(
                NativeMethods.InvokeMessageCreate(
                    handle,
                    authorData,
                    (nuint)author.Length,
                    out reply),
                "The component invocation failed.");
        }

        try
        {
            var length = checked((int)reply.Length);
            var bytes = new byte[length];
            Marshal.Copy(reply.Data, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            NativeMethods.FreeBuffer(reply);
        }
    }

    public void Dispose()
    {
        var runtime = Interlocked.Exchange(ref handle, nint.Zero);
        if (runtime != nint.Zero)
        {
            NativeMethods.Destroy(runtime);
        }

        GC.SuppressFinalize(this);
    }

    private static void ThrowIfFailed(int status, string message)
    {
        if (status != 0)
        {
            throw new RuneNativeException($"{message} Native status: {status}.");
        }
    }
}
