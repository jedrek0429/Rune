using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rune.Core.Invocations;
using Rune.Runtime;

namespace Rune.Bot.Host;

public sealed class RuneResultWorker(
    IRuneTransport transport,
    IRuneHostRequestHandler hostRequestHandler,
    ILogger<RuneResultWorker> logger)
    : BackgroundService
{
    private readonly string _consumerName =
        $"{Environment.MachineName}-{Environment.ProcessId}";

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<RuneResultMessage> messages;

            try
            {
                messages = await transport.ReadResultsAsync(
                    _consumerName,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Failed to read Rune results from Redis.");

                await Task.Delay(
                    TimeSpan.FromMilliseconds(250),
                    stoppingToken);

                continue;
            }

            if (messages.Count == 0)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(25),
                    stoppingToken);

                continue;
            }

            foreach (var message in messages)
            {
                await HandleResultAsync(
                    message,
                    stoppingToken);
            }
        }
    }

    private async ValueTask HandleResultAsync(
        RuneResultMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = message.Result;

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                logger.LogWarning(
                    "Rune {RuneName} ({ExecutionId}) failed in the runner after {DurationMicros} us: {Error}",
                    result.RuneName,
                    result.ExecutionId,
                    result.DurationMicros,
                    result.Error);

                return;
            }

            EventRuneInvocation invocation = RuneEventCodec.FromPayload(
                result.InvocationId,
                result.GuildId,
                result.EventType,
                result.Payload);

            foreach (var action in result.Actions)
            {
                var request = new RuneHostRequest(
                    "host_request",
                    result.InvocationId,
                    action.Method,
                    action.Arguments);

                await hostRequestHandler.HandleAsync(
                    invocation,
                    request,
                    cancellationToken);
            }

            logger.LogDebug(
                "Rune {RuneName} ({ExecutionId}) completed in {DurationMicros} us with {ActionCount} host action(s).",
                result.RuneName,
                result.ExecutionId,
                result.DurationMicros,
                result.Actions.Count);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Failed to apply result {StreamId} returned by a Rune runner.",
                message.StreamId);
        }
        finally
        {
            try
            {
                await transport.AcknowledgeResultAsync(
                    message.StreamId,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(
                    exception,
                    "Failed to acknowledge Rune result {StreamId}.",
                    message.StreamId);
            }
        }
    }
}
