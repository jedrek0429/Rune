using Rune.Api.Generator;

var root = FindRepositoryRoot();
var command = args.FirstOrDefault() ?? "generate";
var model = RuneApiLoader.Load(
    Path.Combine(root, "api", "rune-api.yaml"));
var output = RuneApiEmitter.Emit(model);

switch (command)
{
    case "generate":
        foreach (var (path, content) in output)
        {
            var destination = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, content);
        }

        Console.WriteLine(
            $"Generated {output.Count} Rune.API artefacts ({model.Fingerprint}).");
        break;

    case "verify":
        foreach (var (path, content) in output)
        {
            var destination = Path.Combine(root, path);
            if (!File.Exists(destination) ||
                File.ReadAllText(destination) != content)
            {
                Console.Error.WriteLine($"Generated artefact is stale: {path}");
                return 1;
            }
        }

        Console.WriteLine(
            $"Verified {output.Count} Rune.API artefacts ({model.Fingerprint}).");
        break;

    default:
        Console.Error.WriteLine("Usage: Rune.Api.Generator [generate|verify]");
        return 2;
}

return 0;

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Rune.slnx")))
            return directory.FullName;

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Rune repository root was not found.");
}
