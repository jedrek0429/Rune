using System.Text.Json;
using Rune.Core.Invocations;
using Rune.Core.Runes;
using Rune.Runtime.Native;

namespace Rune.Runtime;

public sealed class RuneEventDispatcher(
    RuneRegistry registry,
    IRuneComponentRuntime runtime,
    IRuneHostRequestHandler hostRequestHandler)
{
    public async ValueTask<IReadOnlyList<RuneFailure>> DispatchAsync(
        EventRuneInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<RuneFailure>();
        var runes = registry.GetEventRunes(
            invocation.GuildId,
            invocation.EventType);

        foreach (var rune in runes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = runtime.Invoke(rune.Id, invocation);

                foreach (var action in result.Actions)
                {
                    var request = ToHostRequest(action, invocation.InvocationId);
                    await hostRequestHandler.HandleAsync(
                        invocation,
                        request,
                        cancellationToken);
                }
            }
            catch (RuneNativeException exception)
            {
                failures.Add(new RuneFailure(rune.Name, exception.Message));
            }
        }

        return failures;
    }

    private static RuneHostRequest ToHostRequest(
        RuneAction action,
        Guid invocationId) =>
        action.Kind switch
        {
            RuneActionKind.Reply => new RuneHostRequest(
                "host_request",
                invocationId,
                "message.reply",
                JsonSerializer.SerializeToElement(
                    new { content = action.Content })),

            _ => throw new InvalidOperationException(
                $"The native runtime returned unsupported action kind '{action.Kind}'.")
        };

    internal static byte[] Serialize(
        EventRuneInvocation invocation)
    {
        object payload = invocation switch
        {
            MessageCreateEventRuneInvocation message =>
                new
                {
                    id = message.MessageId,
                    channelId = message.ChannelId,
                    content = message.Content,
                    author = new
                    {
                        id = message.AuthorId,
                        username = message.AuthorUsername
                    }
                },

            MessageDeleteEventRuneInvocation message =>
                new
                {
                    channelId = message.ChannelId,
                    guildId = (ulong?)message.GuildId,
                    messageId = message.MessageId
                },

            MessageReactionAddEventRuneInvocation reaction =>
                new
                {
                    burst = reaction.Burst,
                    channelId = reaction.ChannelId,
                    emoji = new
                    {
                        animated = reaction.Emoji.Animated,
                        id = reaction.Emoji.Id,
                        name = reaction.Emoji.Name
                    },
                    guildId = (ulong?)reaction.GuildId,
                    messageAuthorId = reaction.MessageAuthorId,
                    messageId = reaction.MessageId,
                    type = reaction.Type,
                    userId = reaction.UserId
                },

            MessageReactionRemoveEventRuneInvocation reaction =>
                new
                {
                    burst = reaction.Burst,
                    channelId = reaction.ChannelId,
                    emoji = new
                    {
                        animated = reaction.Emoji.Animated,
                        id = reaction.Emoji.Id,
                        name = reaction.Emoji.Name
                    },
                    guildId = (ulong?)reaction.GuildId,
                    messageId = reaction.MessageId,
                    type = reaction.Type,
                    userId = reaction.UserId
                },

            _ => throw new ArgumentOutOfRangeException(
                nameof(invocation),
                invocation.EventType,
                "The gateway event is not supported.")
        };

        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }
}

public sealed record RuneFailure(
    string RuneName,
    string Message);
