using Rune.Core.Runes;

namespace Rune.Core.Invocations;

public abstract record EventRuneInvocation(
    Guid InvocationId,
    ulong GuildId)
{
    public abstract RuneEventType EventType { get; }
}

public sealed record MessageCreateEventRuneInvocation(
    Guid InvocationId,
    ulong GuildId,
    ulong ChannelId,
    ulong MessageId,
    ulong AuthorId,
    string AuthorUsername,
    string Content)
    : EventRuneInvocation(InvocationId, GuildId)
{
    public override RuneEventType EventType =>
        RuneEventType.MessageCreate;
}
