namespace Rune.Runtime.Compilation;

public sealed class RuneCompilationOptions
{
    public string JavaScriptCompiler { get; set; } =
        "extism-js";

    public string PythonCompiler { get; set; } =
        "extism-py";

    public string RustCompiler { get; set; } =
        "cargo";

    public TimeSpan JavaScriptTimeout { get; set; } =
        TimeSpan.FromSeconds(30);

    public TimeSpan PythonTimeout { get; set; } =
        TimeSpan.FromSeconds(30);

    public TimeSpan RustTimeout { get; set; } =
        TimeSpan.FromMinutes(2);

    public string RustTargetDirectory { get; set; } =
        Path.Combine(
            Path.GetTempPath(),
            "rune-rust-target");
}
