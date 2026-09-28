using Microsoft.Extensions.Logging;

using NetCord.Hosting.Gateway;

using Rune.Api;
using Rune.Core.Invocations;
using Rune.Runtime;

namespace Rune.Bot.Gateway;

public sealed class MessageDeleteHandler(
    RuneEventDispatcher dispatcher,
    RuneInvocationReceiverRegistry receivers,
    ILogger<MessageDeleteHandler> logger)
    : IMessageDeleteGatewayHandler
{
    public async ValueTask HandleAsync(
        NetCord.Gateway.MessageDeleteEventArgs args)
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
                    new MessageDeleteEventRuneInvocation(
                        invocationId,
                        guildId,
                        payload.ChannelId,
                        payload.MessageId));

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
                "Rune {RuneName} failed during MessageDelete: {Message}",
                failure.RuneName,
                failure.Message);
        }
    }
}
