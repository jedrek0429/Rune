using System.Text.Json;

using Rune.Api;

namespace Rune.Runtime;

public sealed record EventRuneInvocation(
    Guid InvocationId,
    ulong GuildId,
    RuneApiEventType EventType,
    JsonElement Payload);
