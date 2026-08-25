using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using Microsoft.Extensions.Logging;

using Rune.Bot.Api.Generated;
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

        var payload = NetCordRuneApi.Project(args);
        var failures = await dispatcher.DispatchAsync(
            new MessageReactionRemoveEventRuneInvocation(
                Guid.NewGuid(),
                guildId,
                payload.ChannelId,
                payload.MessageId,
                payload.UserId,
                new MessageReactionEmojiInvocation(
                    payload.Emoji.Animated,
                    payload.Emoji.Id,
                    payload.Emoji.Name),
                payload.Burst,
                (byte)payload.Type));

        foreach (var failure in failures)
        {
            logger.LogWarning(
                "Rune {RuneName} failed during MessageReactionRemove: {Message}",
                failure.RuneName,
                failure.Message);
        }
    }
}
