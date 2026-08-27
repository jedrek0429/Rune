using System.Globalization;
using System.Text.Json;
using Rune.Core.Invocations;
using Rune.Core.Runes;

namespace Rune.Runtime;

public static class RuneEventCodec
{
    public static JsonElement ToPayload(EventRuneInvocation invocation) =>
        invocation switch
        {
            MessageCreateEventRuneInvocation message =>
                JsonSerializer.SerializeToElement(new
                {
                    id = Snowflake(message.MessageId),
                    channelId = Snowflake(message.ChannelId),
                    content = message.Content,
                    author = new
                    {
                        id = Snowflake(message.AuthorId),
                        username = message.AuthorUsername
                    }
                }),

            MessageDeleteEventRuneInvocation message =>
                JsonSerializer.SerializeToElement(new
                {
                    channelId = Snowflake(message.ChannelId),
                    guildId = Snowflake(message.GuildId),
                    messageId = Snowflake(message.MessageId)
                }),

            MessageReactionAddEventRuneInvocation reaction =>
                JsonSerializer.SerializeToElement(new
                {
                    burst = reaction.Burst,
                    channelId = Snowflake(reaction.ChannelId),
                    emoji = new
                    {
                        animated = reaction.Emoji.Animated,
                        id = reaction.Emoji.Id is ulong emojiId
                            ? Snowflake(emojiId)
                            : null,
                        name = reaction.Emoji.Name
                    },
                    guildId = Snowflake(reaction.GuildId),
                    messageAuthorId = reaction.MessageAuthorId is ulong authorId
                        ? Snowflake(authorId)
                        : null,
                    messageId = Snowflake(reaction.MessageId),
                    type = reaction.Type,
                    userId = Snowflake(reaction.UserId)
                }),

            MessageReactionRemoveEventRuneInvocation reaction =>
                JsonSerializer.SerializeToElement(new
                {
                    burst = reaction.Burst,
                    channelId = Snowflake(reaction.ChannelId),
                    emoji = new
                    {
                        animated = reaction.Emoji.Animated,
                        id = reaction.Emoji.Id is ulong emojiId
                            ? Snowflake(emojiId)
                            : null,
                        name = reaction.Emoji.Name
                    },
                    guildId = Snowflake(reaction.GuildId),
                    messageId = Snowflake(reaction.MessageId),
                    type = reaction.Type,
                    userId = Snowflake(reaction.UserId)
                }),

            _ => throw new ArgumentOutOfRangeException(
                nameof(invocation),
                invocation.EventType,
                "The gateway event is not supported.")
        };

    public static EventRuneInvocation FromPayload(
        Guid invocationId,
        ulong guildId,
        RuneEventType eventType,
        JsonElement payload) =>
        eventType switch
        {
            RuneEventType.MessageCreate =>
                new MessageCreateEventRuneInvocation(
                    invocationId,
                    guildId,
                    Snowflake(payload, "channelId"),
                    Snowflake(payload, "id"),
                    Snowflake(payload.GetProperty("author"), "id"),
                    payload.GetProperty("author").GetProperty("username").GetString() ?? string.Empty,
                    payload.GetProperty("content").GetString() ?? string.Empty),

            RuneEventType.MessageDelete =>
                new MessageDeleteEventRuneInvocation(
                    invocationId,
                    guildId,
                    Snowflake(payload, "channelId"),
                    Snowflake(payload, "messageId")),

            RuneEventType.MessageReactionAdd =>
                new MessageReactionAddEventRuneInvocation(
                    invocationId,
                    guildId,
                    Snowflake(payload, "channelId"),
                    Snowflake(payload, "messageId"),
                    Snowflake(payload, "userId"),
                    OptionalSnowflake(payload, "messageAuthorId"),
                    Emoji(payload.GetProperty("emoji")),
                    payload.GetProperty("burst").GetBoolean(),
                    payload.GetProperty("type").GetByte()),

            RuneEventType.MessageReactionRemove =>
                new MessageReactionRemoveEventRuneInvocation(
                    invocationId,
                    guildId,
                    Snowflake(payload, "channelId"),
                    Snowflake(payload, "messageId"),
                    Snowflake(payload, "userId"),
                    Emoji(payload.GetProperty("emoji")),
                    payload.GetProperty("burst").GetBoolean(),
                    payload.GetProperty("type").GetByte()),

            _ => throw new ArgumentOutOfRangeException(
                nameof(eventType),
                eventType,
                "The gateway event is not supported.")
        };

    private static MessageReactionEmojiInvocation Emoji(JsonElement value) =>
        new(
            value.GetProperty("animated").GetBoolean(),
            OptionalSnowflake(value, "id"),
            value.TryGetProperty("name", out var name) && name.ValueKind != JsonValueKind.Null
                ? name.GetString()
                : null);

    private static string Snowflake(ulong value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static ulong Snowflake(JsonElement value, string propertyName) =>
        ParseSnowflake(value.GetProperty(propertyName));

    private static ulong? OptionalSnowflake(JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ParseSnowflake(property);
    }

    private static ulong ParseSnowflake(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String =>
                ulong.Parse(
                    value.GetString()!,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture),

            JsonValueKind.Number => value.GetUInt64(),

            _ => throw new JsonException("Snowflake must be encoded as a decimal string or unsigned integer.")
        };
}
