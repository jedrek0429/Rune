using System.Text.Json;

using Rune.Core.Invocations;
using Rune.Core.Runes;
using Rune.Runtime.Exceptions;
using Rune.Runtime.Wasm;

namespace Rune.Runtime;

public sealed class RuneEventDispatcher(
    RuneRegistry registry,
    RuneExecutor executor,
    IRuneHostRequestHandler hostRequestHandler)
{
    public async ValueTask<IReadOnlyList<RuneFailure>>
        DispatchAsync(
            EventRuneInvocation invocation,
            CancellationToken cancellationToken = default)
    {
        var failures =
            new List<RuneFailure>();

        var runes =
            registry.GetEventRunes(
                invocation.GuildId,
                invocation.EventType);
        var input = Serialize(invocation);

        foreach (var rune in runes)
        {
            try
            {
                var result =
                    await executor.ExecuteAsync(
                        rune,
                        invocation.InvocationId,
                        input,
                        cancellationToken);

                // Commit host operations only after the
                // WASM invocation completed successfully.
                foreach (var request in result.Requests)
                {
                    await hostRequestHandler.HandleAsync(
                        invocation,
                        request,
                        cancellationToken);
                }
            }
            catch (RuneTimeoutException exception)
            {
                failures.Add(
                    new RuneFailure(
                        rune.Name,
                        exception.Message));
            }
            catch (RuneExecutionException exception)
            {
                failures.Add(
                    new RuneFailure(
                        rune.Name,
                        exception.Message));
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                // Rune was disabled/removed/updated while
                // this event was being dispatched.
            }
        }

        return failures;
    }

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
