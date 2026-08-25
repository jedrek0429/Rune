using NetCord.Gateway;
using NetCord.Hosting.Gateway;

using Rune.Core.Invocations;
using Rune.Runtime;

namespace Rune.Bot.Gateway;

public sealed class MessageReactionAddHandler(
    RuneEventDispatcher dispatcher,
    ILogger<MessageReactionAddHandler> logger)
    : IMessageReactionAddGatewayHandler
{
    public async ValueTask HandleAsync(
        MessageReactionAddEventArgs args)
    {
        if (args.GuildId is not ulong guildId)
            return;

        var failures = await dispatcher.DispatchAsync(
            new MessageReactionAddEventRuneInvocation(
                Guid.NewGuid(),
                guildId,
                args.ChannelId,
                args.MessageId,
                args.UserId,
                args.MessageAuthorId,
                new MessageReactionEmojiInvocation(
                    args.Emoji.Animated,
                    args.Emoji.Id,
                    args.Emoji.Name),
                args.Burst,
                (byte)args.Type));

        foreach (var failure in failures)
        {
            logger.LogWarning(
                "Rune {RuneName} failed during MessageReactionAdd: {Message}",
                failure.RuneName,
                failure.Message);
        }
    }
}
