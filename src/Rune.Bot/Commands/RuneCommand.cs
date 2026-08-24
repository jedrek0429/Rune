using System.Text;

using NetCord;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;

using Rune.Core.Runes;
using Rune.Runtime.Compilation;
using Rune.Runtime.Exceptions;
using Rune.Runtime.Wasm;

namespace Rune.Bot.Commands;

[SlashCommand(
    "rune",
    "Manage runes")]
public sealed class RuneCommand(
    RuneRegistry registry,
    RuneCompiler compiler,
    RuneWasmCache wasmCache,
    RuneExecutor executor,
    IHttpClientFactory httpClientFactory)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private const int MaxSourceSize =
        64 * 1024;

    [RequireUserPermissions<ApplicationCommandContext>(
        Permissions.ManageGuild)]
    [SubSlashCommand(
        "register",
        "Register a rune")]
    public async Task RegisterAsync(
        string name,
        Attachment file)
    {
        await DeferAsync();

        if (Context.Interaction.GuildId
            is not ulong guildId)
        {
            await FinishAsync(
                "Runes can only be registered in a server.");

            return;
        }

        name = name.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            await FinishAsync(
                "Rune name cannot be empty.");

            return;
        }

        if (registry.Get(
                guildId,
                name) is not null)
        {
            await FinishAsync(
                $"A rune named `{name}` already exists.");

            return;
        }

        var upload =
            await ReadRuneAsync(file);

        if (upload.Error is not null)
        {
            await FinishAsync(upload.Error);
            return;
        }

        try
        {
            var wasm =
                await compiler.CompileAsync(
                    upload.Language!.Value,
                    upload.Source!);

            // Native WASM validation + expected export.
            wasmCache.Validate(wasm);

            var rune =
                new RegisteredRune(
                    Guid.NewGuid(),
                    guildId,
                    name,
                    upload.Language.Value,
                    RuneEventType.MessageCreate,
                    upload.Source!,
                    wasm,
                    true);

            if (!registry.Add(rune))
            {
                await FinishAsync(
                    $"A rune named `{name}` already exists.");

                return;
            }

            await FinishAsync(
                $"Registered `{rune.Name}` ({rune.Language}).");
        }
        catch (RuneCompilationException exception)
        {
            await FinishAsync(
                $"Rune rejected:\n{exception.Message}");
        }
    }

    [RequireUserPermissions<ApplicationCommandContext>(
        Permissions.ManageGuild)]
    [SubSlashCommand(
        "list",
        "List registered runes")]
    public string List()
    {
        if (Context.Interaction.GuildId
            is not ulong guildId)
        {
            return "Runes are scoped to servers.";
        }

        var runes =
            registry.GetRunes(guildId);

        if (runes.Count == 0)
            return "No runes are registered.";

        var lines =
            runes
                .Take(25)
                .Select(rune =>
                    $"{(rune.Enabled ? "●" : "○")} " +
                    $"`{rune.Name}` — " +
                    $"{rune.Language}, {rune.EventType}");

        var result =
            string.Join('\n', lines);

        if (runes.Count > 25)
        {
            result +=
                $"\n…and {runes.Count - 25} more.";
        }

        return result;
    }

    [RequireUserPermissions<ApplicationCommandContext>(
        Permissions.ManageGuild)]
    [SubSlashCommand(
        "info",
        "Show rune information")]
    public string Info(
        string name)
    {
        if (Context.Interaction.GuildId
            is not ulong guildId)
        {
            return "Runes are scoped to servers.";
        }

        var rune =
            registry.Get(
                guildId,
                name);

        if (rune is null)
            return $"Rune `{name}` was not found.";

        var sourceBytes =
            Encoding.UTF8.GetByteCount(
                rune.Source);

        return
            $"**{rune.Name}**\n" +
            $"ID: `{rune.Id}`\n" +
            $"Language: {rune.Language}\n" +
            $"Event: {rune.EventType}\n" +
            $"Status: {(rune.Enabled ? "enabled" : "disabled")}\n" +
            $"Source: {sourceBytes:N0} bytes\n" +
            $"WASM: {rune.Wasm.Length:N0} bytes";
    }

    [RequireUserPermissions<ApplicationCommandContext>(
        Permissions.ManageGuild)]
    [SubSlashCommand(
        "disable",
        "Disable a rune")]
    public async Task<string> DisableAsync(
        string name)
    {
        if (Context.Interaction.GuildId
            is not ulong guildId)
        {
            return "Runes are scoped to servers.";
        }

        if (!registry.SetEnabled(
                guildId,
                name,
                false,
                out var rune))
        {
            return $"Rune `{name}` was not found.";
        }

        await executor.StopAsync(
            rune!.Id);

        return $"Disabled `{rune.Name}`.";
    }

    [RequireUserPermissions<ApplicationCommandContext>(
        Permissions.ManageGuild)]
    [SubSlashCommand(
        "enable",
        "Enable a rune")]
    public string Enable(
        string name)
    {
        if (Context.Interaction.GuildId
            is not ulong guildId)
        {
            return "Runes are scoped to servers.";
        }

        if (!registry.SetEnabled(
                guildId,
                name,
                true,
                out var rune))
        {
            return $"Rune `{name}` was not found.";
        }

        executor.Resume(
            rune!.Id);

        return $"Enabled `{rune.Name}`.";
    }

    [RequireUserPermissions<ApplicationCommandContext>(
        Permissions.ManageGuild)]
    [SubSlashCommand(
        "remove",
        "Remove a rune")]
    public async Task<string> RemoveAsync(
        string name)
    {
        if (Context.Interaction.GuildId
            is not ulong guildId)
        {
            return "Runes are scoped to servers.";
        }

        if (!registry.Remove(
                guildId,
                name,
                out var rune))
        {
            return $"Rune `{name}` was not found.";
        }

        await executor.StopAsync(
            rune!.Id);

        return $"Removed `{rune.Name}`.";
    }

    [RequireUserPermissions<ApplicationCommandContext>(
        Permissions.ManageGuild)]
    [SubSlashCommand(
        "update",
        "Replace a rune's source")]
    public async Task UpdateAsync(
        string name,
        Attachment file)
    {
        await DeferAsync();

        if (Context.Interaction.GuildId
            is not ulong guildId)
        {
            await FinishAsync(
                "Runes are scoped to servers.");

            return;
        }

        var current =
            registry.Get(
                guildId,
                name);

        if (current is null)
        {
            await FinishAsync(
                $"Rune `{name}` was not found.");

            return;
        }

        var upload =
            await ReadRuneAsync(file);

        if (upload.Error is not null)
        {
            await FinishAsync(upload.Error);
            return;
        }

        try
        {
            // Build and validate before touching the currently
            // working version.
            var wasm =
                await compiler.CompileAsync(
                    upload.Language!.Value,
                    upload.Source!);

            wasmCache.Validate(wasm);

            var updated =
                current with
                {
                    Language =
                        upload.Language.Value,

                    Source =
                        upload.Source!,

                    Wasm =
                        wasm
                };

            await executor.StopAsync(
                current.Id);

            registry.Replace(updated);

            if (updated.Enabled)
                executor.Resume(updated.Id);

            await FinishAsync(
                $"Updated `{updated.Name}` ({updated.Language}).");
        }
        catch (RuneCompilationException exception)
        {
            await FinishAsync(
                $"Update rejected:\n{exception.Message}");
        }
    }

    private async Task<RuneUpload> ReadRuneAsync(
        Attachment file)
    {
        if (file.Size > MaxSourceSize)
        {
            return RuneUpload.Fail(
                "Rune source may not exceed 64 KiB.");
        }

        RuneLanguage? language =
            Path.GetExtension(
                    file.FileName)
                .ToLowerInvariant()
            switch
            {
                ".js" or ".mjs" =>
                    RuneLanguage.JavaScript,

                ".py" =>
                    RuneLanguage.Python,

                _ => null
            };

        if (language is null)
        {
            return RuneUpload.Fail(
                "Supported files are `.js`, `.mjs`, and `.py`.");
        }

        try
        {
            var client =
                httpClientFactory.CreateClient();

            using var response =
                await client.GetAsync(
                    file.Url);

            response.EnsureSuccessStatusCode();

            var bytes =
                await response.Content
                    .ReadAsByteArrayAsync();

            if (bytes.Length > MaxSourceSize)
            {
                return RuneUpload.Fail(
                    "Rune source may not exceed 64 KiB.");
            }

            return new RuneUpload(
                language,
                Encoding.UTF8.GetString(
                    bytes),
                null);
        }
        catch (HttpRequestException)
        {
            return RuneUpload.Fail(
                "The uploaded file could not be downloaded.");
        }
    }

    private Task DeferAsync()
    {
        return Context.Interaction
            .SendResponseAsync(
                InteractionCallback.DeferredMessage(
                    MessageFlags.Ephemeral));
    }

    private async Task FinishAsync(
        string content)
    {
        await Context.Interaction
            .ModifyResponseAsync(
                message =>
                    message.Content = content);
    }

    private sealed record RuneUpload(
        RuneLanguage? Language,
        string? Source,
        string? Error)
    {
        public static RuneUpload Fail(
            string error)
            => new(
                null,
                null,
                error);
    }
}
