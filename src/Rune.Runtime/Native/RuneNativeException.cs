namespace Rune.Runtime.Native;

public sealed class RuneNativeException : Exception
{
    internal RuneNativeException(string message)
        : base(message)
    {
        NativeDetail = string.Empty;
    }

    internal RuneNativeException(
        string message,
        int nativeStatus,
        string nativeDetail)
        : base(BuildMessage(message, nativeStatus, nativeDetail))
    {
        NativeStatus = nativeStatus;
        NativeDetail = nativeDetail;
    }

    public int NativeStatus { get; }

    public string NativeDetail { get; }

    private static string BuildMessage(
        string message,
        int nativeStatus,
        string nativeDetail)
    {
        return string.IsNullOrWhiteSpace(nativeDetail)
            ? $"{message} Native status: {nativeStatus}."
            : $"{message} Native status: {nativeStatus}. {nativeDetail}";
    }
}
