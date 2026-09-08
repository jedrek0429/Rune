using System.Text.Json;
using System.Text.Json.Serialization;
using Rune.Core.Invocations;
using StackExchange.Redis;

namespace Rune.Runtime;

public sealed class RedisRuneTransport : IRuneTransport, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private readonly RuneRuntimeOptions _options;
    private readonly ConnectionMultiplexer _connection;
    private readonly IDatabase _database;

    public RedisRuneTransport(RuneRuntimeOptions options)
    {
        _options = options;
        _connection = ConnectionMultiplexer.Connect(options.RedisConnectionString);
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

    public async ValueTask DisposeAsync() =>
        await _connection.DisposeAsync();

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
