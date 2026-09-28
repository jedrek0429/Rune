namespace Rune.Api;

public static class NetCordRuneApi
{
    public static User Project(
        NetCord.User value) =>
        new(
            value.Id,
            value.Username);

    public static Message Project(
        NetCord.Gateway.Message value) =>
        new(
            value.Id,
            value.ChannelId,
            value.Content,
            Project(value.Author));

    public static MessageDeleteEventArgs Project(
        NetCord.Gateway.MessageDeleteEventArgs value) =>
        new(
            value.ChannelId,
            value.GuildId,
            value.MessageId);

    public static MessageReactionEmoji Project(
        NetCord.MessageReactionEmoji value) =>
        new(
            value.Animated,
            value.Id,
            value.Name);

    public static MessageReactionAddEventArgs Project(
        NetCord.Gateway.MessageReactionAddEventArgs value) =>
        new(
            value.Burst,
            value.ChannelId,
            Project(value.Emoji),
            value.GuildId,
            value.MessageAuthorId,
            value.MessageId,
            (ReactionType)(int)value.Type,
            value.UserId);

    public static MessageReactionRemoveEventArgs Project(
        NetCord.Gateway.MessageReactionRemoveEventArgs value) =>
        new(
            value.Burst,
            value.ChannelId,
            Project(value.Emoji),
            value.GuildId,
            value.MessageId,
            (ReactionType)(int)value.Type,
            value.UserId);
}
