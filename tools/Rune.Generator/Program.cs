using Rune.Generator;

if (args.Length != 1 ||
    !string.Equals(
        args[0],
        "generate",
        StringComparison.Ordinal))
{
    Console.Error.WriteLine(
        "usage: Rune.Generator generate");
    return 2;
}

var root = FindRepositoryRoot();

var api =
    RuneApiLoader.Load(
        Path.Combine(
            root,
            "contracts",
            "rune-api.yaml"));

var runtime =
    RuntimeContractLoader.Load(
        Path.Combine(
            root,
            "contracts",
            "runtime.yaml"));

var generated =
    RuneApiEmitter.Emit(api)
        .Concat(
            RuntimeContractEmitter.Emit(
                runtime,
                api));

foreach (var (relativePath, content) in generated)
{
    var path =
        Path.Combine(
            root,
            relativePath.Replace(
                '/',
                Path.DirectorySeparatorChar));

    Directory.CreateDirectory(
        Path.GetDirectoryName(path)!);

    File.WriteAllText(
        path,
        content);
}

return 0;

static string FindRepositoryRoot()
{
    var directory =
        new DirectoryInfo(
            Directory.GetCurrentDirectory());

    while (directory is not null)
    {
        if (File.Exists(
                Path.Combine(
                    directory.FullName,
                    "Rune.slnx")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException(
        "Rune repository root was not found.");
}
