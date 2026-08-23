using NetCord;
using NetCord.Services.ApplicationCommands;

using Rune.Core.Runes;

namespace Rune.Bot.Commands;

[SlashCommand("rune", "Manage runes")]
public sealed class RuneCommand(
    RuneRegistry registry,
    IHttpClientFactory httpClientFactory)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    [SubSlashCommand("register", "Register a rune")]
    public async Task<string> RegisterAsync(
        string name,
        Attachment file)
    {
        if (Context.Guild is null)
            return "Runes can only be registered in servers.";

        if (registry.Exists(Context.Guild.Id, name))
            return $"A rune named `{name}` already exists.";

        var language = Path.GetExtension(file.FileName).ToLowerInvariant() switch
        {
            ".js" or ".mjs" => RuneLanguage.JavaScript,
            ".py" => RuneLanguage.Python,
            _ => (RuneLanguage?)null
        };

        if (language is null)
            return "Unsupported rune language.";

        if (file.Size > 64 * 1024)
            return "Rune files cannot exceed 64 KiB.";

        var client = httpClientFactory.CreateClient();
        var source = await client.GetStringAsync(file.Url);

        registry.Add(new RegisteredRune(
            Id: Guid.NewGuid(),
            GuildId: Context.Guild.Id,
            Name: name,
            Language: language.Value,
            EventType: RuneEventType.MessageCreate,
            Source: source,
            Enabled: true));

        return $"Registered `{name}`.";
    }

    [SubSlashCommand("list", "List registered runes")]
    public string List()
    {
        if (Context.Guild is null)
            return "Rune commands are only available in servers.";

        var runes = registry.GetRunes(Context.Guild.Id);

        if (runes.Count == 0)
            return "No runes are registered.";

        return string.Join(
            '\n',
            runes.Select(rune =>
                $"{rune.Name} — {rune.EventType} — {rune.Language}"));
    }
}
