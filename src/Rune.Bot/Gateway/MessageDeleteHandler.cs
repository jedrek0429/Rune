using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using Microsoft.Extensions.Logging;

using Rune.Core.Invocations;
using Rune.Runtime;

namespace Rune.Bot.Gateway;

public sealed class MessageDeleteHandler(
    RuneEventDispatcher dispatcher,
    ILogger<MessageDeleteHandler> logger)
    : IMessageDeleteGatewayHandler
{
    public async ValueTask HandleAsync(
        MessageDeleteEventArgs args)
    {
        if (args.GuildId is not ulong guildId)
            return;

        var failures = await dispatcher.DispatchAsync(
            new MessageDeleteEventRuneInvocation(
                Guid.NewGuid(),
                guildId,
                args.ChannelId,
                args.MessageId));

        LogFailures(failures);
    }

    private void LogFailures(
        IReadOnlyList<RuneFailure> failures)
    {
        foreach (var failure in failures)
        {
            logger.LogWarning(
                "Rune {RuneName} failed during MessageDelete: {Message}",
                failure.RuneName,
                failure.Message);
        }
    }
}
