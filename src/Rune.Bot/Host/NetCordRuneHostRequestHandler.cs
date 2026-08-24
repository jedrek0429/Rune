using System.Text.Json;

using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

using Rune.Core.Invocations;
using Rune.Runtime;

namespace Rune.Bot.Host;

public sealed class NetCordRuneHostRequestHandler(
    GatewayClient client)
    : IRuneHostRequestHandler
{
    public async ValueTask HandleAsync(
        EventRuneInvocation invocation,
        RuneHostRequest request,
        CancellationToken cancellationToken = default)
    {
        switch (request.Method)
        {
            case "message.reply":
                await HandleMessageReplyAsync(
                    invocation,
                    request.Arguments,
                    cancellationToken);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown Rune API method '{request.Method}'.");
        }
    }

    private async ValueTask HandleMessageReplyAsync(
        EventRuneInvocation invocation,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (invocation is not MessageCreateEventRuneInvocation message)
        {
            throw new InvalidOperationException(
                "message.reply requires a message-create invocation.");
        }

        var content = arguments
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrEmpty(content))
            return;

        /*
         * SECURITY:
         *
         * Ignore any channel/message/guild IDs supplied by the sandbox.
         * The invocation created by Rune.Bot is authoritative.
         */

        if (!client.Cache.Guilds.TryGetValue(
                message.GuildId,
                out var guild))
        {
            throw new InvalidOperationException(
                $"Guild {message.GuildId} is not available.");
        }

        var channels = await guild.GetChannelsAsync(
            cancellationToken: cancellationToken);

        var channel = channels
            .OfType<TextChannel>()
            .FirstOrDefault(channel =>
                channel.Id == message.ChannelId);

        if (channel is null)
        {
            throw new InvalidOperationException(
                $"Channel {message.ChannelId} does not belong to guild {message.GuildId}.");
        }

        var originalMessage = await channel.GetMessageAsync(
            message.MessageId,
            cancellationToken: cancellationToken);

        await originalMessage.ReplyAsync(
            new ReplyMessageProperties
            {
                Content = content
            },
            cancellationToken: cancellationToken);
    }
}
