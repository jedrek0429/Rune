
using System.Text;

namespace Rune.Generator;

public static class RuneApiDocumentationEmitter
{
    private const string OutputRoot =
        "docs/src/content/docs/api/generated";

    public static IReadOnlyDictionary<string, string>
        Emit(RuneApiModel model)
    {
        var output =
            new SortedDictionary<string, string>(
                StringComparer.Ordinal)
            {
                [$"{OutputRoot}/index.mdx"] =
                    EmitIndex(model),

                [$"{OutputRoot}/events.mdx"] =
                    EmitEvents(model),

                ["docs/src/data/generated/api.json"] =
                    EmitVersionData(model)
            };

        foreach (var type in model.Types)
        {
            output[
                $"{OutputRoot}/types/{Slug(type.Name)}.mdx"] =
                EmitType(model, type);
        }

        return output;
    }

    private static string EmitVersionData(
        RuneApiModel model) =>
        "{\n" +
        $"  \"runeApi\": \"{model.Version}\",\n" +
        $"  \"netCord\": \"{model.NetCordVersion}\"\n" +
        "}\n";

    private static string EmitIndex(
        RuneApiModel model)
    {
        var text = new StringBuilder();

        AppendFrontmatter(
            text,
            "API Reference",
            "Generated reference for the language-neutral Rune.Api contract.");

        AppendTabsImport(text);

        text.AppendLine(
            $"**Rune.Api {model.Version}** · **NetCord {model.NetCordVersion}**");
        text.AppendLine();
        text.AppendLine(
            "The API surface is identical in every supported binding. Only language-specific syntax and semantics differ.");
        text.AppendLine();
        text.AppendLine(
            "Choose a language once. The selection is preserved across Rune.Api reference pages.");
        text.AppendLine();

        AppendLanguageSemantics(text);

        text.AppendLine("## Reference");
        text.AppendLine();
        text.AppendLine("- [Events](./events/)");
        text.AppendLine();
        text.AppendLine("### Types");
        text.AppendLine();

        foreach (var type in model.Types)
        {
            text.AppendLine(
                $"- [{type.Name}](./types/{Slug(type.Name)}/)");
        }

        return text.ToString();
    }

    private static string EmitEvents(
        RuneApiModel model)
    {
        var text = new StringBuilder();

        AppendFrontmatter(
            text,
            "Events",
            "Rune.Api gateway events and their payloads.");

        AppendTabsImport(text);

        text.AppendLine("| Event | Payload | Source |");
        text.AppendLine("| --- | --- | --- |");

        foreach (var runeEvent in model.Events)
        {
            text.AppendLine(
                $"| `{runeEvent.Name}` | [{runeEvent.Payload}](../types/{Slug(runeEvent.Payload)}/) | {NetCordSource(runeEvent.NetCordName, NetCordMemberUrl(runeEvent.NetCordName))} |");
        }

        text.AppendLine();
        text.AppendLine("<Tabs syncKey=\"language\">");
        text.AppendLine("  <TabItem label=\"TypeScript\">");
        text.AppendLine();
        text.AppendLine("```ts");
        text.AppendLine("export interface RuneEventArguments {");

        foreach (var runeEvent in model.Events)
        {
            text.AppendLine(
                $"    {runeEvent.Name}: {runeEvent.Payload};");
        }

        text.AppendLine("}");
        text.AppendLine("```");
        text.AppendLine();
        text.AppendLine("  </TabItem>");
        text.AppendLine("  <TabItem label=\"Rust\">");
        text.AppendLine();
        text.AppendLine("```rust");
        text.AppendLine("pub enum RuneEventArguments {");

        foreach (var runeEvent in model.Events)
        {
            text.AppendLine(
                $"    {runeEvent.Name}({runeEvent.Payload}),");
        }

        text.AppendLine("}");
        text.AppendLine("```");
        text.AppendLine();
        text.AppendLine("  </TabItem>");
        text.AppendLine("</Tabs>");

        return text.ToString();
    }

    private static string EmitType(
        RuneApiModel model,
        RuneApiType type)
    {
        var text = new StringBuilder();

        AppendTypeFrontmatter(
            text,
            type,
            type.Summary ??
                $"Rune.Api reference for {type.Name}.");

        AppendTabsImport(text);

        text.AppendLine("## Overview");
        text.AppendLine();

        if (!string.IsNullOrWhiteSpace(type.Summary))
        {
            text.AppendLine(type.Summary);
            text.AppendLine();
        }

        text.AppendLine(
            NetCordSource(
                type.NetCordName,
                NetCordTypeUrl(type.NetCordName)));
        text.AppendLine();

        AppendTypeDeclaration(text, type);
        text.AppendLine();

        if (!type.IsEnum)
        {
            text.AppendLine("### Inheritance");
            text.AppendLine();

            var chain =
                new[] { "object" }
                    .Concat(
                        Inheritance(model, type)
                            .Select(
                                ancestor =>
                                    $"[{ancestor.Name}](../{Slug(ancestor.Name)}/)"))
                    .Append($"**{type.Name}**");

            text.AppendLine(
                string.Join(
                    " → ",
                    chain));
            text.AppendLine();
        }

        var inheritedMembers =
            InheritedMembers(model, type);

        var inheritedMethods =
            InheritedMethods(model, type);

        if (inheritedMembers.Count > 0 ||
            inheritedMethods.Count > 0)
        {
            text.AppendLine("### Inherited members");
            text.AppendLine();

            foreach (var member in inheritedMembers)
            {
                AppendMemberLink(text, member.Owner, member.Member.Name);
            }

            foreach (var method in inheritedMethods)
            {
                AppendMethodLink(text, method.Owner, method.Method.Name);
            }

            text.AppendLine();
        }

        AppendExamples(text, type.Examples, "## Example");

        if (!type.IsEnum && type.Members.Count > 0)
        {
            text.AppendLine("## Properties");
            text.AppendLine();

            foreach (var member in type.Members)
            {
                text.AppendLine($"### {member.Name}");
                text.AppendLine();

                if (!string.IsNullOrWhiteSpace(member.Summary))
                {
                    text.AppendLine(member.Summary);
                    text.AppendLine();
                }

                AppendPropertySignature(text, member);
                text.AppendLine();
                text.AppendLine("#### Property Value");
                text.AppendLine();
                AppendValueType(text, model, member.Type);
                text.AppendLine();
                text.AppendLine(
                    NetCordSource(
                        member.CanonicalId,
                        NetCordMemberUrl(member.CanonicalId)));
                text.AppendLine();
            }
        }

        if (type.Methods.Count > 0)
        {
            text.AppendLine("## Methods");
            text.AppendLine();

            foreach (var method in type.Methods)
            {
                text.AppendLine($"### {RuneApiEmitter.TypeScriptMethodForDocumentation(method.Name)}");
                text.AppendLine();

                if (!string.IsNullOrWhiteSpace(method.Summary))
                {
                    text.AppendLine(method.Summary);
                    text.AppendLine();
                }

                AppendMethodSignature(text, method);
                text.AppendLine();

                if (method.Parameters.Count > 0)
                {
                    text.AppendLine("#### Parameters");
                    text.AppendLine();

                    foreach (var parameter in method.Parameters)
                    {
                        text.AppendLine(
                            $"**`{RuneApiEmitter.TypeScriptMemberForDocumentation(parameter.Name)}`**");
                        text.AppendLine();
                        AppendValueType(text, model, parameter.Type);
                        text.AppendLine();
                    }
                }

                text.AppendLine("#### Returns");
                text.AppendLine();
                AppendValueType(text, model, method.Result);
                text.AppendLine();
                text.AppendLine(
                    $"Host operation: `{method.HostName}`  ");
                text.AppendLine(
                    NetCordSource(
                        method.CanonicalId,
                        NetCordMemberUrl(method.CanonicalId)));
                text.AppendLine();

                AppendExamples(text, method.Examples, "#### Example");
            }
        }

        if (type.IsEnum)
        {
            text.AppendLine("## Values");
            text.AppendLine();

            foreach (var member in type.Members)
            {
                text.AppendLine(
                    $"- `{member.Name}` = `{member.EnumValue}`" +
                    (string.IsNullOrWhiteSpace(member.Summary)
                        ? string.Empty
                        : $" — {member.Summary}"));
            }
        }

        return text.ToString();
    }

    private static void AppendTypeDeclaration(
        StringBuilder text,
        RuneApiType type)
    {
        var typeScript =
            type.IsEnum
                ? $"export enum {type.Name}"
                : $"export class {type.Name}" +
                  (type.Base is null
                      ? string.Empty
                      : $" extends {type.Base}");

        var rust =
            type.IsEnum
                ? $"pub enum {type.Name}"
                : $"pub struct {type.Name}";

        text.AppendLine("<Tabs syncKey=\"language\">");
        text.AppendLine("  <TabItem label=\"TypeScript\">");
        text.AppendLine();
        text.AppendLine($"```ts\n{typeScript}\n```");
        text.AppendLine();
        text.AppendLine("  </TabItem>");
        text.AppendLine("  <TabItem label=\"Rust\">");
        text.AppendLine();
        text.AppendLine($"```rust\n{rust}\n```");
        text.AppendLine();
        text.AppendLine("  </TabItem>");
        text.AppendLine("</Tabs>");
    }

    private static void AppendExamples(
        StringBuilder text,
        IReadOnlyDictionary<string, string> examples,
        string heading)
    {
        if (examples.Count == 0)
            return;

        text.AppendLine(heading);
        text.AppendLine();
        text.AppendLine("<Tabs syncKey=\"language\">");

        foreach (var language in new[] { "typescript", "rust" })
        {
            if (!examples.TryGetValue(language, out var example))
                continue;

            var label =
                language == "typescript"
                    ? "TypeScript"
                    : "Rust";

            var fence =
                language == "typescript"
                    ? "ts"
                    : "rust";

            text.AppendLine($"  <TabItem label=\"{label}\">");
            text.AppendLine();
            text.AppendLine($"```{fence}");
            text.AppendLine(example.TrimEnd());
            text.AppendLine("```");
            text.AppendLine();
            text.AppendLine("  </TabItem>");
        }

        text.AppendLine("</Tabs>");
        text.AppendLine();
    }

    private static void AppendValueType(
        StringBuilder text,
        RuneApiModel model,
        RuneApiValueType valueType)
    {
        text.AppendLine("<Tabs syncKey=\"language\">");
        text.AppendLine("  <TabItem label=\"TypeScript\">");
        text.AppendLine();

        if (valueType.IsSelectedType)
        {
            text.AppendLine(
                $"[{valueType.Name}](../{Slug(valueType.Name)}/)");
        }
        else
        {
            text.AppendLine(
                $"`{RuneApiEmitter.TypeScriptTypeForDocumentation(valueType)}`");
        }

        text.AppendLine();
        text.AppendLine("  </TabItem>");
        text.AppendLine("  <TabItem label=\"Rust\">");
        text.AppendLine();

        if (valueType.IsSelectedType)
        {
            text.AppendLine(
                $"[{valueType.Name}](../{Slug(valueType.Name)}/)");
        }
        else
        {
            text.AppendLine(
                $"`{RuneApiEmitter.RustTypeForDocumentation(valueType)}`");
        }

        text.AppendLine();
        text.AppendLine("  </TabItem>");
        text.AppendLine("</Tabs>");

        if (valueType.IsSelectedType)
        {
            var selected =
                model.Types.Single(
                    type =>
                        type.Name == valueType.Name);

            text.AppendLine();
            text.AppendLine(
                NetCordSource(
                    selected.NetCordName,
                    NetCordTypeUrl(selected.NetCordName)));
        }
    }

    private static void AppendPropertySignature(
        StringBuilder text,
        RuneApiMember member)
    {
        text.AppendLine("<Tabs syncKey=\"language\">");
        text.AppendLine("  <TabItem label=\"TypeScript\">");
        text.AppendLine();
        text.AppendLine(
            $"```ts\nreadonly {RuneApiEmitter.TypeScriptMemberForDocumentation(member.Name)}: {RuneApiEmitter.TypeScriptTypeForDocumentation(member.Type)};\n```");
        text.AppendLine();
        text.AppendLine("  </TabItem>");
        text.AppendLine("  <TabItem label=\"Rust\">");
        text.AppendLine();
        text.AppendLine(
            $"```rust\npub {RuneApiEmitter.RustMemberForDocumentation(member.Name)}: {RuneApiEmitter.RustTypeForDocumentation(member.Type)},\n```");
        text.AppendLine();
        text.AppendLine("  </TabItem>");
        text.AppendLine("</Tabs>");
    }

    private static void AppendMethodSignature(
        StringBuilder text,
        RuneApiMethod method)
    {
        var tsParameters =
            string.Join(
                ", ",
                method.Parameters.Select(
                    parameter =>
                        $"{RuneApiEmitter.TypeScriptMemberForDocumentation(parameter.Name)}: " +
                        $"{RuneApiEmitter.TypeScriptTypeForDocumentation(parameter.Type)}"));

        var rustParameters =
            string.Join(
                ", ",
                method.Parameters.Select(
                    parameter =>
                        $"{RuneApiEmitter.RustMemberForDocumentation(parameter.Name)}: " +
                        $"{RuneApiEmitter.RustTypeForDocumentation(parameter.Type)}"));

        if (rustParameters.Length > 0)
            rustParameters = ", " + rustParameters;

        text.AppendLine("<Tabs syncKey=\"language\">");
        text.AppendLine("  <TabItem label=\"TypeScript\">");
        text.AppendLine();
        text.AppendLine(
            $"```ts\n{RuneApiEmitter.TypeScriptMethodForDocumentation(method.Name)}({tsParameters}): Promise<{RuneApiEmitter.TypeScriptTypeForDocumentation(method.Result)}>;\n```");
        text.AppendLine();
        text.AppendLine("  </TabItem>");
        text.AppendLine("  <TabItem label=\"Rust\">");
        text.AppendLine();
        text.AppendLine(
            $"```rust\n{RuneApiEmitter.RustMethodForDocumentation(method.Name)}(&self, host: &mut dyn RuneHost{rustParameters}) -> Result<{RuneApiEmitter.RustTypeForDocumentation(method.Result)}, String>;\n```");
        text.AppendLine();
        text.AppendLine("  </TabItem>");
        text.AppendLine("</Tabs>");
    }

    private static IReadOnlyList<RuneApiType>
        Inheritance(
            RuneApiModel model,
            RuneApiType type)
    {
        var result = new List<RuneApiType>();
        var current = type;

        while (current.Base is not null)
        {
            current =
                model.Types.Single(
                    candidate =>
                        candidate.Name == current.Base);

            result.Insert(0, current);
        }

        return result;
    }

    private static IReadOnlyList<(RuneApiType Owner, RuneApiMember Member)>
        InheritedMembers(
            RuneApiModel model,
            RuneApiType type)
    {
        return Inheritance(model, type)
            .SelectMany(
                owner =>
                    owner.Members.Select(
                        member =>
                            (owner, member)))
            .ToArray();
    }

    private static IReadOnlyList<(RuneApiType Owner, RuneApiMethod Method)>
        InheritedMethods(
            RuneApiModel model,
            RuneApiType type)
    {
        return Inheritance(model, type)
            .SelectMany(
                owner =>
                    owner.Methods.Select(
                        method =>
                            (owner, method)))
            .ToArray();
    }

    private static void AppendMemberLink(
        StringBuilder text,
        RuneApiType owner,
        string memberName)
    {
        text.AppendLine(
            $"- [{owner.Name}.{memberName}](../{Slug(owner.Name)}/#{Slug(memberName)})");
    }

    private static void AppendMethodLink(
        StringBuilder text,
        RuneApiType owner,
        string methodName)
    {
        var name =
            RuneApiEmitter.TypeScriptMethodForDocumentation(
                methodName);

        text.AppendLine(
            $"- [{owner.Name}.{name}](../{Slug(owner.Name)}/#{Slug(name)})");
    }

    private static void AppendLanguageSemantics(
        StringBuilder text)
    {
        text.AppendLine("<Tabs syncKey=\"language\">");
        text.AppendLine("  <TabItem label=\"TypeScript\">");
        text.AppendLine();
        text.AppendLine(
            "TypeScript uses camelCase members, string snowflakes, Promise-returning host calls, and object-bound host access.");
        text.AppendLine();
        text.AppendLine("  </TabItem>");
        text.AppendLine("  <TabItem label=\"Rust\">");
        text.AppendLine();
        text.AppendLine(
            "Rust uses snake_case members, native `u64` snowflakes, `Result<T, String>` host calls, and an explicit mutable host reference.");
        text.AppendLine();
        text.AppendLine("  </TabItem>");
        text.AppendLine("</Tabs>");
        text.AppendLine();
    }

    private static void AppendTypeFrontmatter(
        StringBuilder text,
        RuneApiType type,
        string description)
    {
        text.AppendLine("---");
        text.AppendLine(
            $"title: {(type.IsEnum ? "Enum" : "Class")} {type.Name}");
        text.AppendLine($"description: {description}");
        text.AppendLine("sidebar:");
        text.AppendLine($"  label: {type.Name}");
        text.AppendLine("---");
        text.AppendLine();
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

    private static void AppendTabsImport(
        StringBuilder text)
    {
        text.AppendLine(
            "import { Tabs, TabItem } from '@astrojs/starlight/components';");
        text.AppendLine();
    }

    private static string NetCordSource(
        string canonicalId,
        string url) =>
        $"<a class=\"netcord-source\" href=\"{url}\" target=\"_blank\" rel=\"noreferrer\" " +
        $"aria-label=\"Open {canonicalId} in NetCord documentation\" " +
        $"title=\"{canonicalId}\">" +
        "<img src=\"https://raw.githubusercontent.com/NetCordDev/NetCord/main/Resources/Logo/svg/SmallSquare.svg\" " +
        "alt=\"\" aria-hidden=\"true\" />" +
        $"<span>{NetCordDisplayName(canonicalId)}</span>" +
        "<span class=\"netcord-source-arrow\" aria-hidden=\"true\">↗</span>" +
        "</a>";

    private static string NetCordDisplayName(
        string canonicalId) =>
        canonicalId.StartsWith(
            "NetCord.",
            StringComparison.Ordinal)
            ? canonicalId["NetCord.".Length..]
            : canonicalId;

    private static string NetCordTypeUrl(
        string canonicalType) =>
        $"https://netcord.dev/docs/{canonicalType}.html";

    private static string NetCordMemberUrl(
        string canonicalMember)
    {
        var separator =
            canonicalMember.LastIndexOf('.');

        var canonicalType =
            separator > 0
                ? canonicalMember[..separator]
                : canonicalMember;

        return NetCordTypeUrl(canonicalType);
    }

    private static string Slug(
        string value) =>
        value.ToLowerInvariant();
}
