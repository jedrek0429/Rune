
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rune.Generator;

public static class RuneBotDocumentationEmitter
{
    private const string OutputRoot =
        "docs/src/content/docs/bot/generated";

    public static IReadOnlyDictionary<string, string>
        Emit(string repositoryRoot)
    {
        var commandsRoot =
            Path.Combine(
                repositoryRoot,
                "src",
                "Rune.Bot",
                "Commands");

        var commands =
            Directory.EnumerateFiles(
                    commandsRoot,
                    "*.cs",
                    SearchOption.AllDirectories)
                .SelectMany(ParseFile)
                .OrderBy(
                    command => command.Name,
                    StringComparer.Ordinal)
                .ToArray();

        var output =
            new SortedDictionary<string, string>(
                StringComparer.Ordinal)
            {
                [$"{OutputRoot}/index.mdx"] =
                    EmitIndex(commands)
            };

        foreach (var command in commands)
        {
            output[
                $"{OutputRoot}/commands/{Slug(command.Name)}.mdx"] =
                EmitCommand(command);
        }

        return output;
    }

    private static IEnumerable<BotCommand>
        ParseFile(string path)
    {
        var source =
            File.ReadAllText(path);

        var root =
            CSharpSyntaxTree
                .ParseText(source)
                .GetCompilationUnitRoot();

        foreach (var type in
                 root.DescendantNodes()
                     .OfType<ClassDeclarationSyntax>())
        {
            var rootAttribute =
                FindAttribute(
                    type.AttributeLists,
                    "SlashCommand");

            if (rootAttribute is null)
                continue;

            var rootName =
                StringArgument(
                    rootAttribute,
                    0);

            var rootDescription =
                StringArgument(
                    rootAttribute,
                    1);

            var subcommands =
                type.Members
                    .OfType<MethodDeclarationSyntax>()
                    .Select(ParseSubcommand)
                    .Where(
                        command =>
                            command is not null)
                    .Cast<BotSubcommand>()
                    .OrderBy(
                        command => command.Name,
                        StringComparer.Ordinal)
                    .ToArray();

            yield return new BotCommand(
                rootName,
                rootDescription,
                subcommands);
        }
    }

    private static BotSubcommand?
        ParseSubcommand(
            MethodDeclarationSyntax method)
    {
        var commandAttribute =
            FindAttribute(
                method.AttributeLists,
                "SubSlashCommand");

        if (commandAttribute is null)
            return null;

        var documentation =
            ParseDocumentation(method);

        var permissionAttribute =
            FindAttribute(
                method.AttributeLists,
                "RequireUserPermissions");

        var permission =
            permissionAttribute?
                .ArgumentList?
                .Arguments
                .FirstOrDefault()?
                .Expression
                .ToString()
                .Split('.')
                .Last();

        var parameters =
            method.ParameterList
                .Parameters
                .Select(
                    parameter =>
                        new BotParameter(
                            parameter.Identifier.ValueText,
                            parameter.Type?.ToString() ??
                            "unknown",
                            documentation.Parameters
                                .GetValueOrDefault(
                                    parameter.Identifier.ValueText)))
                .ToArray();

        return new BotSubcommand(
            StringArgument(
                commandAttribute,
                0),
            StringArgument(
                commandAttribute,
                1),
            documentation.Summary,
            permission,
            parameters,
            documentation.Examples);
    }

    private static BotDocumentation
        ParseDocumentation(
            MethodDeclarationSyntax method)
    {
        var trivia =
            method.AttributeLists.Count > 0
                ? method.AttributeLists[0]
                    .GetLeadingTrivia()
                    .ToFullString()
                : method.GetLeadingTrivia()
                    .ToFullString();

        var xml =
            string.Join(
                '\n',
                trivia
                    .Split('\n')
                    .Select(
                        line =>
                            Regex.Replace(
                                line,
                                @"^\s*/// ?",
                                string.Empty))
                    .Where(
                        line =>
                            !string.IsNullOrWhiteSpace(line)));

        if (string.IsNullOrWhiteSpace(xml))
        {
            return new BotDocumentation(
                null,
                new Dictionary<string, string>(
                    StringComparer.Ordinal),
                []);
        }

        try
        {
            var document =
                XDocument.Parse(
                    $"<doc>{xml}</doc>");

            var summary =
                Normalize(
                    document.Root?
                        .Element("summary")?
                        .Value);

            var parameters =
                document.Root?
                    .Elements("param")
                    .Where(
                        element =>
                            element.Attribute("name") is not null)
                    .ToDictionary(
                        element =>
                            element.Attribute("name")!.Value,
                        element =>
                            Normalize(element.Value) ??
                            string.Empty,
                        StringComparer.Ordinal) ??
                new Dictionary<string, string>(
                    StringComparer.Ordinal);

            var examples =
                document.Root?
                    .Elements("example")
                    .Select(
                        example =>
                            Normalize(example.Value))
                    .Where(
                        example =>
                            !string.IsNullOrWhiteSpace(example))
                    .Cast<string>()
                    .ToArray() ??
                [];

            return new BotDocumentation(
                summary,
                parameters,
                examples);
        }
        catch
        {
            return new BotDocumentation(
                null,
                new Dictionary<string, string>(
                    StringComparer.Ordinal),
                []);
        }
    }

    private static string EmitIndex(
        IReadOnlyList<BotCommand> commands)
    {
        var text = new StringBuilder();

        AppendFrontmatter(
            text,
            "Bot Reference",
            "Commands for managing runes in Discord.");

        text.AppendLine(
            "Use the Rune bot to register, inspect, enable, update, and remove runes.");
        text.AppendLine();
        text.AppendLine(
            "Choose a command below for its options and examples.");
        text.AppendLine();

        foreach (var command in commands)
        {
            text.AppendLine(
                $"- [/{command.Name}](./commands/{Slug(command.Name)}/) — {command.Description}");
        }

        return text.ToString();
    }

    private static string EmitCommand(
        BotCommand command)
    {
        var text = new StringBuilder();

        AppendFrontmatter(
            text,
            $"/{command.Name}",
            command.Description);

        text.AppendLine(command.Description);
        text.AppendLine();

        foreach (var subcommand in command.Subcommands)
        {
            text.AppendLine(
                $"## /{command.Name} {subcommand.Name}");
            text.AppendLine();

            text.AppendLine(
                subcommand.Summary ??
                subcommand.Description);
            text.AppendLine();

            if (subcommand.Permission is not null)
            {
                text.AppendLine(
                    $"**Required permission:** `{HumanizePermission(subcommand.Permission)}`");
                text.AppendLine();
            }

            if (subcommand.Parameters.Count > 0)
            {
                text.AppendLine("### Options");
                text.AppendLine();
                text.AppendLine("| Option | Type | Description |");
                text.AppendLine("| --- | --- | --- |");

                foreach (var parameter in subcommand.Parameters)
                {
                    text.AppendLine(
                        $"| `{parameter.Name}` | `{FriendlyType(parameter.Type)}` | {parameter.Description ?? "—"} |");
                }

                text.AppendLine();
            }

            if (subcommand.Examples.Count > 0)
            {
                text.AppendLine("### Examples");
                text.AppendLine();

                foreach (var example in subcommand.Examples)
                {
                    text.AppendLine("```text");
                    text.AppendLine(example);
                    text.AppendLine("```");
                    text.AppendLine();
                }
            }
        }

        return text.ToString();
    }

    private static AttributeSyntax?
        FindAttribute(
            SyntaxList<AttributeListSyntax> lists,
            string name)
    {
        return lists
            .SelectMany(
                list =>
                    list.Attributes)
            .FirstOrDefault(
                attribute =>
                {
                    var value =
                        attribute.Name.ToString();

                    return value == name ||
                           value == name + "Attribute" ||
                           value.StartsWith(
                               name + "<",
                               StringComparison.Ordinal);
                });
    }

    private static string StringArgument(
        AttributeSyntax attribute,
        int index)
    {
        var argument =
            attribute.ArgumentList?
                .Arguments
                .ElementAtOrDefault(index)?
                .Expression as LiteralExpressionSyntax;

        return argument?.Token.ValueText ??
               string.Empty;
    }

    private static string FriendlyType(
        string type) =>
        type switch
        {
            "string" => "Text",
            "Attachment" => "File",
            _ => type
        };

    private static string HumanizePermission(
        string permission) =>
        permission switch
        {
            "ManageGuild" => "Manage Server",
            _ => permission
        };

    private static string? Normalize(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return Regex.Replace(
                value.Trim(),
                @"\s+",
                " ");
    }

    private static void AppendFrontmatter(
        StringBuilder text,
        string title,
        string description)
    {
        text.AppendLine("---");
        text.AppendLine($"title: {title}");
        text.AppendLine($"description: {description}");
        text.AppendLine("---");
        text.AppendLine();
    }

    private static string Slug(
        string value) =>
        value.ToLowerInvariant();

    private sealed record BotCommand(
        string Name,
        string Description,
        IReadOnlyList<BotSubcommand> Subcommands);

    private sealed record BotSubcommand(
        string Name,
        string Description,
        string? Summary,
        string? Permission,
        IReadOnlyList<BotParameter> Parameters,
        IReadOnlyList<string> Examples);

    private sealed record BotParameter(
        string Name,
        string Type,
        string? Description);

    private sealed record BotDocumentation(
        string? Summary,
        IReadOnlyDictionary<string, string> Parameters,
        IReadOnlyList<string> Examples);
}
