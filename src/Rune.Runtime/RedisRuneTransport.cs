using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;

namespace Rune.Runtime;

public sealed class RedisRuneTransport : IRuneTransport, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private readonly RuneRedisOptions _options;
    private readonly ConnectionMultiplexer _connection;
    private readonly IDatabase _database;
    private readonly SemaphoreSlim _resultGroupGate = new(1, 1);
    private bool _resultGroupReady;

    public RedisRuneTransport(RuneRedisOptions options)
    {
        _options = options;
        _connection = ConnectionMultiplexer.Connect(options.ConnectionString);
        _database = _connection.GetDatabase();
    }

    public async ValueTask EnqueueAsync(
        RuneInvocationEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var json = JsonSerializer.Serialize(envelope, SerializerOptions);
        await _database.StreamAddAsync(
            _options.InvocationStream,
            [new NameValueEntry("json", json)]);
    }

    public async ValueTask<IReadOnlyList<RuneResultMessage>> ReadResultsAsync(
        string consumerName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureResultConsumerGroupAsync();

        var entries = await _database.StreamReadGroupAsync(
            _options.ResultStream,
            _options.ResultConsumerGroup,
            consumerName,
            ">",
            count: _options.ResultBatchSize);

        if (entries.Length == 0)
            return Array.Empty<RuneResultMessage>();

        var results = new List<RuneResultMessage>(entries.Length);

        foreach (var entry in entries)
        {
            var json = entry.Values
                .FirstOrDefault(value => value.Name == "json")
                .Value;

            if (json.IsNullOrEmpty)
                continue;

            var result = JsonSerializer.Deserialize<RuneResultEnvelope>(
                json.ToString(),
                SerializerOptions);

            if (result is null)
                continue;

            results.Add(new RuneResultMessage(entry.Id.ToString(), result));
        }

        return results;
    }

    public async ValueTask AcknowledgeResultAsync(
        string streamId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _database.StreamAcknowledgeAsync(
            _options.ResultStream,
            _options.ResultConsumerGroup,
            streamId);

        await _database.StreamDeleteAsync(
            _options.ResultStream,
            [streamId]);
    }

    public async ValueTask DisposeAsync()
    {
        _resultGroupGate.Dispose();
        await _connection.DisposeAsync();
    }

    private async ValueTask EnsureResultConsumerGroupAsync()
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
                await _database.StreamCreateConsumerGroupAsync(
                    _options.ResultStream,
                    _options.ResultConsumerGroup,
                    "0-0",
                    createStream: true);
            }
            catch (RedisServerException exception)
                when (exception.Message.Contains("BUSYGROUP", StringComparison.Ordinal))
            {
            }

            _resultGroupReady = true;
        }
        finally
        {
            _resultGroupGate.Release();
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
