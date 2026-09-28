using System.Text.Json;

using NetCord.Rest;

using Rune.Runtime;

namespace Rune.Bot.Host;

public sealed class NetCordRuneHostRequestHandler(
    RuneInvocationReceiverRegistry receivers)
    : IRuneHostRequestHandler
{
    public async ValueTask HandleAsync(
        RuneHostRequest request,
        CancellationToken cancellationToken = default)
    {
        switch (request.Method)
        {
            case "message.reply":
                await HandleMessageReplyAsync(
                    request,
                    cancellationToken);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown Rune API method '{request.Method}'.");
        }
    }

    private async ValueTask
        HandleMessageReplyAsync(
            RuneHostRequest request,
            CancellationToken cancellationToken)
    {
        var message =
            receivers.GetRequired<
                NetCord.Gateway.Message>(
                request.InvocationId);

        var replyMessage =
            request.Arguments
                .GetProperty("replyMessage");

        var content =
            replyMessage.TryGetProperty(
                    "content",
                    out var value) &&
                value.ValueKind is not
                    JsonValueKind.Null
                    ? value.GetString()
                    : null;

        if (string.IsNullOrEmpty(content))
            return;

        await message.ReplyAsync(
            new ReplyMessageProperties
            {
                Content = content
            },
            cancellationToken:
                cancellationToken);
    }
}
