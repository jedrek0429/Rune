using Microsoft.Extensions.Logging;

using NetCord.Hosting.Gateway;

using Rune.Api;
using Rune.Core.Invocations;
using Rune.Runtime;

namespace Rune.Bot.Gateway;

public sealed class MessageReactionAddHandler(
    RuneEventDispatcher dispatcher,
    ILogger<MessageReactionAddHandler> logger)
    : IMessageReactionAddGatewayHandler
{
    public async ValueTask HandleAsync(
        NetCord.Gateway.MessageReactionAddEventArgs args)
    {
        if (args.GuildId is not ulong guildId)
            return;

        var payload =
            NetCordRuneApi.Project(args);

        var failures =
            await dispatcher.DispatchAsync(
                new MessageReactionAddEventRuneInvocation(
                    Guid.NewGuid(),
                    guildId,
                    payload.ChannelId,
                    payload.MessageId,
                    payload.UserId,
                    payload.MessageAuthorId,
                    new MessageReactionEmojiInvocation(
                        payload.Emoji.Animated,
                        payload.Emoji.Id,
                        payload.Emoji.Name),
                    payload.Burst,
                    (byte)payload.Type));

        foreach (var failure in failures)
        {
            logger.LogWarning(
                "Rune {RuneName} failed during MessageReactionAdd: {Message}",
                failure.RuneName,
                failure.Message);
        }
    }
}
