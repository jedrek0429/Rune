namespace Rune.Api;

public sealed record User(
    ulong Id,
    string Username);

public sealed record ReplyMessageProperties(
    string? Content);

public record RestMessage(
    ulong Id,
    ulong ChannelId,
    string Content,
    User Author);

public sealed record Message(
    ulong Id,
    ulong ChannelId,
    string Content,
    User Author)
    : RestMessage(
        Id,
        ChannelId,
        Content,
        Author);

public sealed record MessageDeleteEventArgs(
    ulong ChannelId,
    ulong? GuildId,
    ulong MessageId);

public sealed record MessageReactionEmoji(
    bool Animated,
    ulong? Id,
    string? Name);

public enum ReactionType
{
    Normal = 0,
    Burst = 1
}

public sealed record MessageReactionAddEventArgs(
    bool Burst,
    ulong ChannelId,
    MessageReactionEmoji Emoji,
    ulong? GuildId,
    ulong? MessageAuthorId,
    ulong MessageId,
    ReactionType Type,
    ulong UserId);

public sealed record MessageReactionRemoveEventArgs(
    bool Burst,
    ulong ChannelId,
    MessageReactionEmoji Emoji,
    ulong? GuildId,
    ulong MessageId,
    ReactionType Type,
    ulong UserId);

public enum RuneApiEvent
{
    MessageCreate,
    MessageDelete,
    MessageReactionAdd,
    MessageReactionRemove
}
