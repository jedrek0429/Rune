using NetCord.Gateway;
using NetCord.Hosting.Gateway;

using Rune.Core.Invocations;
using Rune.Runtime;

namespace Rune.Bot.Gateway;

public sealed class MessageReactionRemoveHandler(
    RuneEventDispatcher dispatcher,
    ILogger<MessageReactionRemoveHandler> logger)
    : IMessageReactionRemoveGatewayHandler
{
    public async ValueTask HandleAsync(
        MessageReactionRemoveEventArgs args)
    {
        if (args.GuildId is not ulong guildId)
            return;

        var failures = await dispatcher.DispatchAsync(
            new MessageReactionRemoveEventRuneInvocation(
                Guid.NewGuid(),
                guildId,
                args.ChannelId,
                args.MessageId,
                args.UserId,
                new MessageReactionEmojiInvocation(
                    args.Emoji.Animated,
                    args.Emoji.Id,
                    args.Emoji.Name),
                args.Burst,
                (byte)args.Type));

        foreach (var failure in failures)
        {
            logger.LogWarning(
                "Rune {RuneName} failed during MessageReactionRemove: {Message}",
                failure.RuneName,
                failure.Message);
        }
    }
}
