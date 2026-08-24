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

    public unsafe RuneInvocationResult InvokeMessageCreate(string authorUsername)
    {
        ArgumentNullException.ThrowIfNull(authorUsername);
        ObjectDisposedException.ThrowIf(handle == nint.Zero, this);

        var author = Encoding.UTF8.GetBytes(authorUsername);
        NativeActionList nativeActions;
        int status;

        fixed (byte* authorData = author)
        {
            status = NativeMethods.InvokeMessageCreate(
                handle,
                authorData,
                (nuint)author.Length,
                out nativeActions);
        }

        try
        {
            ThrowIfFailed(status, "The component invocation failed.");
            return DecodeActions(nativeActions);
        }
        finally
        {
            NativeMethods.FreeActionList(nativeActions);
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

    private static unsafe RuneInvocationResult DecodeActions(NativeActionList nativeActions)
    {
        var length = checked((int)nativeActions.Length);
        if (length > 0 && nativeActions.Data == nint.Zero)
        {
            throw new RuneNativeException("The native runtime returned an invalid action list.");
        }

        var actions = new RuneAction[length];
        var nativeAction = (NativeAction*)nativeActions.Data;

        for (var index = 0; index < length; index++)
        {
            var kind = (RuneActionKind)nativeAction[index].Kind;
            actions[index] = new RuneAction(
                kind,
                DecodeBuffer(nativeAction[index].Content));
        }

        return new RuneInvocationResult(actions);
    }

    private static string DecodeBuffer(NativeBuffer buffer)
    {
        var length = checked((int)buffer.Length);
        if (length == 0)
        {
            return string.Empty;
        }

        if (buffer.Data == nint.Zero)
        {
            throw new RuneNativeException("The native runtime returned an invalid buffer.");
        }

        var bytes = new byte[length];
        Marshal.Copy(buffer.Data, bytes, 0, length);
        return Encoding.UTF8.GetString(bytes);
    }

    private static void ThrowIfFailed(int status, string message)
    {
        if (status != 0)
        {
            throw new RuneNativeException($"{message} Native status: {status}.");
        }
    }
}
