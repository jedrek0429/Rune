using System.Text.Json;

using Rune.Core.Invocations;
using Rune.Core.Runes;
using Rune.Runtime.Exceptions;
using Rune.Runtime.Protocol;
using Rune.Runtime.Sandbox;

namespace Rune.Runtime;

public sealed class RuneEventDispatcher(
    RuneRegistry registry,
    SandboxManager sandboxes,
    IRuneHostRequestHandler hostRequestHandler)
{
    public async ValueTask DispatchAsync(
        EventRuneInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        var runes = registry.GetEventRunes(
            invocation.GuildId,
            invocation.EventType);

        foreach (var rune in runes)
        {
            await sandboxes.UseAsync(
                rune,
                async (sandbox, cancellationToken) =>
                {
                    if (invocation is not MessageCreateEventRuneInvocation message)
                        return;

                    var payload = new
                    {
                        type = "message_create",
                        invocationId = invocation.InvocationId,

                        message = new
                        {
                            id = message.MessageId,
                            channelId = message.ChannelId,
                            content = message.Content,

                            author = new
                            {
                                id = message.AuthorId,
                                username = message.AuthorUsername
                            }
                        }
                    };

                    var json = JsonSerializer.Serialize(payload);

                    await sandbox.SendAsync(
                        json,
                        cancellationToken);

                    var completed = false;

                    await foreach (
                        var line in sandbox.ReadAsync(cancellationToken))
                    {
                        using var document = JsonDocument.Parse(line);

                        var root = document.RootElement;

                        var type = root
                            .GetProperty("type")
                            .GetString();

                        if (type == "complete")
                        {
                            completed = true;
                            break;
                        }

                        if (type != "request")
                            continue;

                        var request =
                            JsonSerializer.Deserialize<RuneHostRequest>(
                                line,
                                new JsonSerializerOptions
                                {
                                    PropertyNameCaseInsensitive = true
                                });

                        if (request is null ||
                            request.InvocationId != invocation.InvocationId)
                        {
                            continue;
                        }

                        await hostRequestHandler.HandleAsync(
                            invocation,
                            request,
                            cancellationToken);
                    }

                    if (!completed)
                    {
                        throw new SandboxCommunicationException(
                            "Sandbox exited before completing the invocation.");
                    }
                },
                cancellationToken);
        }
    }
}