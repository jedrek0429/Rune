using System.Text;

using NetCord;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;

using Rune.Runtime;
using Rune.Runtime.Exceptions;

namespace Rune.Bot.Commands;

[SlashCommand(
    "rune",
    "Manage runes")]
public sealed class RuneCommand(
    RuneRegistry runeRegistry,
    RuneService runeService,
    RuneUploadReader uploadReader)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    /// <summary>
    /// Uploads source code and registers it as a rune for this server.
    /// Rune detects the language from the file extension and compiles the source before registration completes.
    /// </summary>
    /// <param name="name">The rune's name in this server.</param>
    /// <param name="file">The rune source file. Supported extensions: .js, .mjs, .ts, .py, .rs, .c, .cc, .cpp, and .cxx.</param>
    /// <example>/rune register name:hello file:hello.ts</example>
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

        if (runeRegistry.Get(
                guildId,
                name) is not null)
        {
            await FinishAsync(
                $"A rune named `{name}` already exists.");

            return;
        }

        var upload =
            await uploadReader.ReadAsync(file);

        if (upload.Error is not null)
        {
            await FinishAsync(upload.Error);
            return;
        }

        try
        {
            var rune =
                await runeService.RegisterAsync(
                    guildId,
                    name,
                    upload.Language!.Value,
                    upload.Source!);

            await FinishAsync(
                $"Registered `{rune.Name}` ({rune.Language}).");
        }
        catch (RuneCompilationException exception)
        {
            await FinishAsync(
                $"Rune rejected:\n{exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            await FinishAsync(exception.Message);
        }
    }

    /// <summary>
    /// Lists runes in this server with their language, event, and status.
    /// </summary>
    /// <example>/rune list</example>
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
            runeRegistry.GetRunes(guildId);

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

    /// <summary>
    /// Shows a rune's language, event, status, and size.
    /// </summary>
    /// <param name="name">The name of the rune to inspect.</param>
    /// <example>/rune info name:hello</example>
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
            runeRegistry.Get(
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
            $"Artifact: {(rune.Artifact?.SizeBytes ?? 0):N0} bytes";
    }

    /// <summary>
    /// Disables a rune and keeps it registered.
    /// </summary>
    /// <param name="name">The name of the rune to disable.</param>
    /// <example>/rune disable name:hello</example>
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

        var rune =
            await runeService.SetEnabledAsync(
                guildId,
                name,
                false);

        return rune is null
            ? $"Rune `{name}` was not found."
            : $"Disabled `{rune.Name}`.";
    }

    /// <summary>
    /// Enables a rune.
    /// </summary>
    /// <param name="name">The name of the rune to enable.</param>
    /// <example>/rune enable name:hello</example>
    [RequireUserPermissions<ApplicationCommandContext>(
        Permissions.ManageGuild)]
    [SubSlashCommand(
        "enable",
        "Enable a rune")]
    public async Task<string> EnableAsync(
        string name)
    {
        if (Context.Interaction.GuildId
            is not ulong guildId)
        {
            return "Runes are scoped to servers.";
        }

        var rune =
            await runeService.SetEnabledAsync(
                guildId,
                name,
                true);

        return rune is null
            ? $"Rune `{name}` was not found."
            : $"Enabled `{rune.Name}`.";
    }

    /// <summary>
    /// Removes a rune from this server.
    /// </summary>
    /// <param name="name">The name of the rune to remove.</param>
    /// <example>/rune remove name:hello</example>
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

        var rune =
            await runeService.RemoveAsync(
                guildId,
                name);

        return rune is null
            ? $"Rune `{name}` was not found."
            : $"Removed `{rune.Name}`.";
    }

    /// <summary>
    /// Replaces a rune's source file and rebuilds it.
    /// </summary>
    /// <param name="name">The name of the rune to update.</param>
    /// <param name="file">The replacement source file.</param>
    /// <example>/rune update name:hello file:hello.ts</example>
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
            runeRegistry.Get(
                guildId,
                name);

        if (current is null)
        {
            await FinishAsync(
                $"Rune `{name}` was not found.");

            return;
        }

        var upload =
            await uploadReader.ReadAsync(file);

        if (upload.Error is not null)
        {
            await FinishAsync(upload.Error);
            return;
        }

        try
        {
            var updated =
                await runeService.UpdateAsync(
                    current,
                    upload.Language!.Value,
                    upload.Source!);

            await FinishAsync(
                $"Updated `{updated.Name}` ({updated.Language}).");
        }
        catch (RuneCompilationException exception)
        {
            await FinishAsync(
                $"Update rejected:\n{exception.Message}");
        }
    }

    private Task DeferAsync()
    {
        return Context.Interaction
            .SendResponseAsync(
                InteractionCallback.DeferredMessage(
                    MessageFlags.Ephemeral));
    }

    private Task FinishAsync(
        string content)
    {
        return Context.Interaction
            .ModifyResponseAsync(
                message =>
                    message.Content = content);
    }
}
