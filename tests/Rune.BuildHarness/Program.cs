using Rune.Api;
using Rune.Runtime;

if (args.Length != 3)
{
    Console.Error.WriteLine(
        "usage: Rune.BuildHarness <language> <event> <source>");
    return 2;
}

var language =
    Enum.Parse<RuneLanguage>(
        args[0],
        ignoreCase: true);

var eventType =
    Enum.Parse<RuneApiEventType>(
        args[1],
        ignoreCase: true);

var source =
    await File.ReadAllTextAsync(
        args[2]);

var artifact =
    await new FirecrackerRuneBuilder()
        .BuildAsync(
            language,
            eventType,
            source);

Console.WriteLine(
    $"{artifact.Id} {artifact.SizeBytes} {artifact.Entrypoint}");

return 0;
