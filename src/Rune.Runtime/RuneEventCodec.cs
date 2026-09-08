using System.Globalization;
using System.Text.Json;
using Rune.Core.Invocations;

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
                        id = reaction.Emoji.Id is ulong emojiId ? Snowflake(emojiId) : null,
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
                        id = reaction.Emoji.Id is ulong emojiId ? Snowflake(emojiId) : null,
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

    private static string Snowflake(ulong value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
