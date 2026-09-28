using System.Text.Json;

namespace Rune.Runtime;

public sealed record RuneHostRequest(
    Guid InvocationId,
    string Method,
    JsonElement Arguments);
