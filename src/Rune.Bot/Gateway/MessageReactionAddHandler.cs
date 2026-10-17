using Microsoft.Extensions.Logging;

using NetCord.Hosting.Gateway;

using Rune.Api;
using Rune.Runtime;

namespace Rune.Bot.Gateway;

public sealed class MessageReactionAddHandler(
    RuneEventDispatcher dispatcher,
    RuneInvocationReceiverRegistry receivers,
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
                        RuneApiEventType.MessageReactionAdd,
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
                "Rune {RuneName} failed during MessageReactionAdd: {Message}",
                failure.RuneName,
                failure.Message);
        }
    }
}
