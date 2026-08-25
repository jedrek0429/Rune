using System.Text.Json;

using Rune.Core.Invocations;
using Xunit;

namespace Rune.Runtime.Tests;

public sealed class EventInvocationSerializationTests
{
    [Fact]
    public void Message_create_preserves_selected_message_and_user_members()
    {
        var payload = Serialize(
            new MessageCreateEventRuneInvocation(
                Guid.NewGuid(),
                1,
                222222222222222222,
                111111111111111111,
                333333333333333333,
                "Ada",
                "hello from Rune"));

        Assert.Equal(
            111111111111111111,
            payload.GetProperty("id").GetUInt64());
        Assert.Equal(
            222222222222222222,
            payload.GetProperty("channelId").GetUInt64());
        Assert.Equal(
            "hello from Rune",
            payload.GetProperty("content").GetString());

        var author = payload.GetProperty("author");
        Assert.Equal(
            333333333333333333,
            author.GetProperty("id").GetUInt64());
        Assert.Equal(
            "Ada",
            author.GetProperty("username").GetString());
    }

    [Fact]
    public void Message_delete_preserves_actual_event_argument_members()
    {
        var payload = Serialize(
            new MessageDeleteEventRuneInvocation(
                Guid.NewGuid(),
                444444444444444444,
                555555555555555555,
                666666666666666666));

        Assert.Equal(
            555555555555555555,
            payload.GetProperty("channelId").GetUInt64());
        Assert.Equal(
            444444444444444444,
            payload.GetProperty("guildId").GetUInt64());
        Assert.Equal(
            666666666666666666,
            payload.GetProperty("messageId").GetUInt64());
    }

    [Fact]
    public void Message_reaction_add_preserves_selected_event_argument_members()
    {
        var payload = Serialize(
            new MessageReactionAddEventRuneInvocation(
                Guid.NewGuid(),
                777777777777777777,
                888888888888888888,
                999999999999999999,
                111111111111111112,
                222222222222222223,
                new MessageReactionEmojiInvocation(
                    true,
                    333333333333333334,
                    "party"),
                true,
                1));

        Assert.True(payload.GetProperty("burst").GetBoolean());
        Assert.Equal(
            222222222222222223,
            payload.GetProperty("messageAuthorId").GetUInt64());
        Assert.Equal(1, payload.GetProperty("type").GetByte());
        AssertEmoji(
            payload.GetProperty("emoji"),
            animated: true,
            id: 333333333333333334,
            name: "party");
    }

    [Fact]
    public void Message_reaction_remove_preserves_standard_emoji_and_type()
    {
        var payload = Serialize(
            new MessageReactionRemoveEventRuneInvocation(
                Guid.NewGuid(),
                777777777777777777,
                888888888888888888,
                999999999999999999,
                111111111111111112,
                new MessageReactionEmojiInvocation(
                    false,
                    null,
                    "⬆️"),
                false,
                0));

        Assert.False(payload.GetProperty("burst").GetBoolean());
        Assert.Equal(0, payload.GetProperty("type").GetByte());
        AssertEmoji(
            payload.GetProperty("emoji"),
            animated: false,
            id: null,
            name: "⬆️");
    }

    private static JsonElement Serialize(
        EventRuneInvocation invocation)
    {
        using var document = JsonDocument.Parse(
            RuneEventDispatcher.Serialize(invocation));
        return document.RootElement.Clone();
    }

    private static void AssertEmoji(
        JsonElement emoji,
        bool animated,
        ulong? id,
        string name)
    {
        Assert.Equal(
            animated,
            emoji.GetProperty("animated").GetBoolean());

        if (id is ulong expectedId)
        {
            Assert.Equal(
                expectedId,
                emoji.GetProperty("id").GetUInt64());
        }
        else
        {
            Assert.Equal(
                JsonValueKind.Null,
                emoji.GetProperty("id").ValueKind);
        }

        Assert.Equal(
            name,
            emoji.GetProperty("name").GetString());
    }
}
