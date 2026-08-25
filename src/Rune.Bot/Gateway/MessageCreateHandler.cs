using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Rest;
using Rune.Bot.Api.Generated;
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

        var payload = NetCordRuneApi.Project(message);
        var invocation =
            new MessageCreateEventRuneInvocation(
                Guid.NewGuid(),
                guildId,
                payload.ChannelId,
                payload.Id,
                payload.Author.Id,
                payload.Author.Username,
                payload.Content);

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
