using NetCord.Hosting.Gateway;

using Rune.Api;
using Rune.Core.Invocations;
using Rune.Runtime;

namespace Rune.Bot.Gateway;

public sealed class MessageCreateHandler(
    RuneEventDispatcher dispatcher,
    RuneInvocationReceiverRegistry receivers)
    : IMessageCreateGatewayHandler
{
    public async ValueTask HandleAsync(
        NetCord.Gateway.Message message)
    {
        if (message.GuildId is not ulong guildId ||
            message.Author.IsBot)
        {
            return;
        }

        var payload =
            NetCordRuneApi.Project(message);

        var invocationId =
            Guid.NewGuid();

        receivers.Register(
            invocationId,
            message);

        RuneDispatchResult result;

        try
        {
            result =
                await dispatcher.DispatchAsync(
                    new MessageCreateEventRuneInvocation(
                        invocationId,
                        guildId,
                        payload.ChannelId,
                        payload.Id,
                        payload.Author.Id,
                        payload.Author.Username,
                        payload.Content));

            receivers.Seal(
                invocationId,
                result.QueuedExecutions);
        }
        catch
        {
            receivers.Cancel(invocationId);
            throw;
        }

        if (result.Failures.Count == 0)
            return;

        var text =
            string.Join(
                '\n',
                result.Failures
                    .Take(3)
                    .Select(
                        failure =>
                            $"{failure.RuneName}: {failure.Message}"));

        await message.ReplyAsync(
            new NetCord.Rest.ReplyMessageProperties
            {
                Content = text
            });
    }
}
