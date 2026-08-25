using System.Runtime.InteropServices;
using System.Text;

using Rune.Core.Invocations;
using Rune.Core.Runes;

namespace Rune.Runtime.Native;

public sealed class RuneNativeRuntime : IDisposable
{
    private static readonly Guid LegacyRuneId =
        new("9857d586-d123-49a3-98a2-1fc65fb3c0d4");

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

    public unsafe void LoadComponent(
        RuneEventType eventType,
        ReadOnlySpan<byte> component) =>
        LoadComponent(LegacyRuneId, eventType, component);

    public unsafe void LoadComponent(
        Guid runeId,
        RuneEventType eventType,
        ReadOnlySpan<byte> component)
    {
        ObjectDisposedException.ThrowIf(handle == nint.Zero, this);

        Span<byte> runeIdBytes = stackalloc byte[16];
        runeId.TryWriteBytes(runeIdBytes);

        fixed (byte* runeIdData = runeIdBytes)
        fixed (byte* componentData = component)
        {
            ThrowIfFailed(
                NativeMethods.LoadComponent(
                    handle,
                    runeIdData,
                    (uint)eventType,
                    componentData,
                    (nuint)component.Length),
                "The component could not be loaded.",
                string.Empty);
        }
    }

    public unsafe bool RemoveComponent(Guid runeId)
    {
        ObjectDisposedException.ThrowIf(handle == nint.Zero, this);

        Span<byte> runeIdBytes = stackalloc byte[16];
        runeId.TryWriteBytes(runeIdBytes);

        fixed (byte* runeIdData = runeIdBytes)
        {
            var status = NativeMethods.RemoveComponent(handle, runeIdData);
            if (status == 4)
                return false;

            ThrowIfFailed(
                status,
                "The component could not be removed.",
                string.Empty);
            return true;
        }
    }

    public RuneInvocationResult Invoke(
        EventRuneInvocation invocation) =>
        Invoke(LegacyRuneId, invocation);

    public RuneInvocationResult Invoke(
        Guid runeId,
        EventRuneInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ObjectDisposedException.ThrowIf(handle == nint.Zero, this);

        var payload = RuneEventDispatcher.Serialize(invocation);

        return InvokePayload(runeId, invocation.EventType, payload);
    }

    private unsafe RuneInvocationResult InvokePayload(
        Guid runeId,
        RuneEventType eventType,
        byte[] payload)
    {
        NativeActionList nativeActions;
        NativeBuffer nativeError;
        int status;

        Span<byte> runeIdBytes = stackalloc byte[16];
        runeId.TryWriteBytes(runeIdBytes);

        fixed (byte* runeIdData = runeIdBytes)
        fixed (byte* payloadData = payload)
        {
            status = NativeMethods.Invoke(
                handle,
                runeIdData,
                (uint)eventType,
                payloadData,
                (nuint)payload.Length,
                out nativeActions,
                out nativeError);
        }

        try
        {
            ThrowIfFailed(
                status,
                "The component invocation failed.",
                DecodeBuffer(nativeError));
            return DecodeActions(nativeActions);
        }
        finally
        {
            NativeMethods.FreeActionList(nativeActions);
            NativeMethods.FreeBuffer(nativeError);
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

    private static void ThrowIfFailed(
        int status,
        string message,
        string nativeDetail)
    {
        if (status != 0)
        {
            throw new RuneNativeException(message, status, nativeDetail);
        }
    }
}
