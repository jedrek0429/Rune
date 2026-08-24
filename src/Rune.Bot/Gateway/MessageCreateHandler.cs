using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Rest;
using Rune.Core.Invocations;
using Rune.Runtime;

namespace Rune.Bot.Gateway;

public sealed class MessageCreateHandler(
    RuneEventDispatcher dispatcher)
    : IMessageCreateGatewayHandler
{
    public async ValueTask HandleAsync(
        Message message)
    {
        if (message.GuildId is not ulong guildId ||
            message.Author.IsBot)
        {
            return;
        }

        var invocation =
            new MessageCreateEventRuneInvocation(
                Guid.NewGuid(),
                guildId,
                message.ChannelId,
                message.Id,
                message.Author.Id,
                message.Author.Username,
                message.Content);

        var failures =
            await dispatcher.DispatchAsync(
                invocation);

        if (failures.Count == 0)
            return;

        var text =
            string.Join(
                '\n',
                failures
                    .Take(3)
                    .Select(failure =>
                        $"`{failure.RuneName}`: {failure.Message}"));

        await message.ReplyAsync(
            new ReplyMessageProperties
            {
                Content = text
            });
    }
}
