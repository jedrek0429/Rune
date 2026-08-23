using NetCord.Gateway;
using NetCord.Hosting.Gateway;

using Rune.Core.Invocations;
using Rune.Runtime;

namespace Rune.Bot.Gateway;

public sealed class MessageCreateHandler(
    RuneEventDispatcher dispatcher)
    : IMessageCreateGatewayHandler
{
    public ValueTask HandleAsync(Message message)
    {
        if (message.Author.IsBot)
            return ValueTask.CompletedTask;
        if (message.GuildId is not ulong guildId)
            return ValueTask.CompletedTask;

        var invocation = new MessageCreateEventRuneInvocation(
            InvocationId: Guid.NewGuid(),
            GuildId: guildId,
            ChannelId: message.ChannelId,
            MessageId: message.Id,
            AuthorId: message.Author.Id,
            AuthorUsername: message.Author.Username,
            Content: message.Content);

        return dispatcher.DispatchAsync(invocation);
    }
}
