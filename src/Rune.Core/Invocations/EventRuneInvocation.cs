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

public sealed record MessageDeleteEventRuneInvocation(
    Guid InvocationId,
    ulong GuildId,
    ulong ChannelId,
    ulong MessageId)
    : EventRuneInvocation(InvocationId, GuildId)
{
    public override RuneEventType EventType =>
        RuneEventType.MessageDelete;
}

public sealed record MessageReactionEmojiInvocation(
    bool Animated,
    ulong? Id,
    string? Name);

public sealed record MessageReactionAddEventRuneInvocation(
    Guid InvocationId,
    ulong GuildId,
    ulong ChannelId,
    ulong MessageId,
    ulong UserId,
    ulong? MessageAuthorId,
    MessageReactionEmojiInvocation Emoji,
    bool Burst,
    byte Type)
    : EventRuneInvocation(InvocationId, GuildId)
{
    public override RuneEventType EventType =>
        RuneEventType.MessageReactionAdd;
}

public sealed record MessageReactionRemoveEventRuneInvocation(
    Guid InvocationId,
    ulong GuildId,
    ulong ChannelId,
    ulong MessageId,
    ulong UserId,
    MessageReactionEmojiInvocation Emoji,
    bool Burst,
    byte Type)
    : EventRuneInvocation(InvocationId, GuildId)
{
    public override RuneEventType EventType =>
        RuneEventType.MessageReactionRemove;
}
