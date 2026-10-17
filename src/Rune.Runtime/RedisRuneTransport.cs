using System.Text.Json;
using System.Text.Json.Serialization;

using StackExchange.Redis;

namespace Rune.Runtime;

public sealed class RedisRuneTransport
    : IRuneTransport,
      IAsyncDisposable
{
    private static readonly JsonSerializerOptions
        SerializerOptions =
            CreateSerializerOptions();

    private readonly RuneRuntimeOptions _options;
    private readonly ConnectionMultiplexer _connection;
    private readonly IDatabase _database;
    private readonly SemaphoreSlim _resultGroupGate =
        new(1, 1);

    private bool _resultGroupReady;

    public RedisRuneTransport(
        RuneRuntimeOptions options)
    {
        _options = options;

        _connection =
            ConnectionMultiplexer.Connect(
                NormaliseConnectionString(
                    options.RedisConnectionString));

        _database =
            _connection.GetDatabase();
    }

    public async ValueTask EnqueueAsync(
        RuneInvocationEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        var json =
            JsonSerializer.Serialize(
                envelope,
                SerializerOptions);

        await _database.StreamAddAsync(
            RuntimeProtocol.InvocationStream,
            [
                new NameValueEntry(
                    "json",
                    json)
            ]);
    }

    public async ValueTask<
        IReadOnlyList<RuneResultMessage>>
        ReadResultsAsync(
            string consumerName,
            CancellationToken cancellationToken = default)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        await EnsureResultConsumerGroupAsync();

        var entries =
            await _database.StreamReadGroupAsync(
                RuntimeProtocol.ResultStream,
                _options.ResultConsumerGroup,
                consumerName,
                ">",
                count:
                    _options.ResultBatchSize);

        if (entries.Length == 0)
        {
            return Array.Empty<
                RuneResultMessage>();
        }

        var results =
            new List<RuneResultMessage>(
                entries.Length);

        foreach (var entry in entries)
        {
            var value =
                entry.Values
                    .FirstOrDefault(
                        pair =>
                            pair.Name == "json")
                    .Value;

            if (value.IsNullOrEmpty)
                continue;

            var result =
                JsonSerializer.Deserialize<
                    RuneResultEnvelope>(
                    value.ToString(),
                    SerializerOptions);

            if (result is null)
                continue;

            results.Add(
                new RuneResultMessage(
                    entry.Id.ToString(),
                    result));
        }

        return results;
    }

    public async ValueTask
        AcknowledgeResultAsync(
            string streamId,
            CancellationToken cancellationToken = default)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        await _database
            .StreamAcknowledgeAsync(
                RuntimeProtocol.ResultStream,
                _options.ResultConsumerGroup,
                streamId);

        await _database
            .StreamDeleteAsync(
                RuntimeProtocol.ResultStream,
                [streamId]);
    }

    public async ValueTask DisposeAsync()
    {
        _resultGroupGate.Dispose();

        await _connection
            .DisposeAsync();
    }

    private async ValueTask
        EnsureResultConsumerGroupAsync()
    {
        if (_resultGroupReady)
            return;

        await _resultGroupGate.WaitAsync();

        try
        {
            if (_resultGroupReady)
                return;

            try
            {
                await _database
                    .StreamCreateConsumerGroupAsync(
                        RuntimeProtocol.ResultStream,
                        _options.ResultConsumerGroup,
                        "0-0",
                        createStream: true);
            }
            catch (RedisServerException exception)
                when (
                    exception.Message.Contains(
                        "BUSYGROUP",
                        StringComparison.Ordinal))
            {
            }

            _resultGroupReady = true;
        }
        finally
        {
            _resultGroupGate.Release();
        }
    }

    private static string
        NormaliseConnectionString(
            string value)
    {
        if (!Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var uri) ||
            uri.Scheme is not
                ("redis" or "rediss"))
        {
            return value;
        }

        var endpoint =
            uri.IsDefaultPort
                ? uri.Host
                : $"{uri.Host}:{uri.Port}";

        return uri.Scheme == "rediss"
            ? $"{endpoint},ssl=true"
            : endpoint;
    }

    private static JsonSerializerOptions
        CreateSerializerOptions()
    {
        var options =
            new JsonSerializerOptions
            {
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase
            };

        options.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase));

        return options;
    }
}
