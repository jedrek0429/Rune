namespace Rune.Runtime.Compilation;

public sealed class RuneCompilationOptions
{
    public string JavaScriptCompiler { get; set; } =
        "jco";

    public string PythonCompiler { get; set; } =
        "componentize-py";

    public string RustCompiler { get; set; } =
        "cargo";

    public string RuneApiWitPath { get; set; } =
        Path.GetFullPath(
            Path.Combine("wit", "rune-api.wit"));

    public string GeneratedApiRoot { get; set; } =
        Path.GetFullPath("generated");

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
