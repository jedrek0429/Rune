using System.Text.Json;

using Rune.Api;

using Xunit;

namespace Rune.Api.Tests;

public sealed class RuneApiContractTests
{
    [Fact]
    public void Message_preserves_selected_inheritance()
    {
        Assert.True(
            typeof(RestMessage)
                .IsAssignableFrom(
                    typeof(Message)));
    }

    [Fact]
    public void Reaction_enum_matches_NetCord()
    {
        Assert.Equal(
            (int)NetCord.ReactionType.Normal,
            (int)ReactionType.Normal);

        Assert.Equal(
            (int)NetCord.ReactionType.Burst,
            (int)ReactionType.Burst);
    }

    [Fact]
    public void Event_identity_contains_the_selected_gateway_events()
    {
        Assert.Equal(
            [
                RuneApiEventType.MessageCreate,
                RuneApiEventType.MessageDelete,
                RuneApiEventType.MessageReactionAdd,
                RuneApiEventType.MessageReactionRemove
            ],
            Enum.GetValues<RuneApiEventType>());
    }

    [Fact]
    public void Message_wire_payload_uses_string_snowflakes()
    {
        var payload =
            RuneApiPayload.Serialize(
                new Message(
                    3,
                    2,
                    "hello",
                    new User(
                        4,
                        "user")));

        Assert.Equal(
            "3",
            payload
                .GetProperty("id")
                .GetString());

        Assert.Equal(
            "2",
            payload
                .GetProperty("channelId")
                .GetString());

        Assert.Equal(
            "4",
            payload
                .GetProperty("author")
                .GetProperty("id")
                .GetString());
    }

    [Fact]
    public void Optional_snowflakes_and_enums_keep_the_wire_contract()
    {
        var payload =
            RuneApiPayload.Serialize(
                new MessageReactionAddEventArgs(
                    false,
                    2,
                    new MessageReactionEmoji(
                        false,
                        6,
                        "x"),
                    null,
                    5,
                    3,
                    ReactionType.Burst,
                    4));

        Assert.Equal(
            JsonValueKind.Null,
            payload
                .GetProperty("guildId")
                .ValueKind);

        Assert.Equal(
            "5",
            payload
                .GetProperty("messageAuthorId")
                .GetString());

        Assert.Equal(
            1,
            payload
                .GetProperty("type")
                .GetInt32());
    }
}
