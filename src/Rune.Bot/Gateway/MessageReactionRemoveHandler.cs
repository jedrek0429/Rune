using Microsoft.Extensions.Logging;

using NetCord.Hosting.Gateway;

using Rune.Api;
using Rune.Core.Invocations;
using Rune.Runtime;

namespace Rune.Bot.Gateway;

public sealed class MessageReactionRemoveHandler(
    RuneEventDispatcher dispatcher,
    RuneInvocationReceiverRegistry receivers,
    ILogger<MessageReactionRemoveHandler> logger)
    : IMessageReactionRemoveGatewayHandler
{
    public async ValueTask HandleAsync(
        NetCord.Gateway.MessageReactionRemoveEventArgs args)
    {
        if (args.GuildId is not ulong guildId)
            return;

        var payload =
            NetCordRuneApi.Project(args);

        var invocationId =
            Guid.NewGuid();

        receivers.Register(
            invocationId,
            args);

        RuneDispatchResult result;

        try
        {
            result =
                await dispatcher.DispatchAsync(
                    new MessageReactionRemoveEventRuneInvocation(
                        invocationId,
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

            receivers.Seal(
                invocationId,
                result.QueuedExecutions);
        }
        catch
        {
            receivers.Cancel(invocationId);
            throw;
        }

        foreach (var failure in result.Failures)
        {
            logger.LogWarning(
                "Rune {RuneName} failed during MessageReactionRemove: {Message}",
                failure.RuneName,
                failure.Message);
        }
    }
}
