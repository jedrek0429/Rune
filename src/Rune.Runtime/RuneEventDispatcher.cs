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

        foreach (var rune in runes)
        {
            if (invocation is not
                MessageCreateEventRuneInvocation message)
            {
                continue;
            }

            var input =
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        message = new
                        {
                            id = message.MessageId,
                            channelId =
                                message.ChannelId,
                            content =
                                message.Content,

                            author = new
                            {
                                id =
                                    message.AuthorId,

                                username =
                                    message.AuthorUsername
                            }
                        }
                    });

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
}

public sealed record RuneFailure(
    string RuneName,
    string Message);
