namespace Rune.Runtime.Native;

public static class RuneNativeRuntime
{
    public static uint AbiVersion =>
        NativeMethods.GetAbiVersion();
}
