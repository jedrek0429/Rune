using Microsoft.Extensions.Logging;

using NetCord.Hosting.Gateway;

using Rune.Api;
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
                    new EventRuneInvocation(
                        invocationId,
                        guildId,
                        RuneApiEventType.MessageReactionRemove,
                        RuneApiPayload.Serialize(payload)));

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
