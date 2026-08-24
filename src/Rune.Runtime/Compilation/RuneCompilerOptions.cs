namespace Rune.Runtime.Compilation;

public sealed class RuneCompilerOptions
{
    public string JavaScriptCompiler { get; init; } = "extism-js";

    public string PythonCompiler { get; init; } = "extism-py";

    public TimeSpan Timeout { get; init; } =
        TimeSpan.FromSeconds(30);
}
