using System.Text.Json;

namespace Rune.Runtime.Protocol;

public sealed record RuneHostRequest(
    string Type,
    Guid InvocationId,
    string Method,
    JsonElement Arguments
);

