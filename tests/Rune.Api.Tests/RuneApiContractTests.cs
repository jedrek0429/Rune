using Rune.Api;
using Xunit;

namespace Rune.Api.Tests;

public sealed class RuneApiContractTests
{
    private static readonly string Root =
        FindRepositoryRoot();

    [Fact]
    public void Manifest_maps_the_four_gateway_events()
    {
        var model = Load();

        Assert.Equal(
            [
                "MessageCreate",
                "MessageDelete",
                "MessageReactionAdd",
                "MessageReactionRemove"
            ],
            model.Events.Select(
                value => value.Name));

        Assert.Equal(
            [
                "Message",
                "MessageDeleteEventArgs",
                "MessageReactionAddEventArgs",
                "MessageReactionRemoveEventArgs"
            ],
            model.Events.Select(
                value => value.Payload));
    }

    [Fact]
    public void Message_preserves_selected_NetCord_inheritance()
    {
        var model = Load();

        var message =
            Assert.Single(
                model.Types,
                value =>
                    value.Name == "Message");

        Assert.Equal(
            "NetCord.Gateway.Message",
            message.NetCordName);

        Assert.Equal(
            "RestMessage",
            message.Base);

        Assert.True(
            typeof(RestMessage)
                .IsAssignableFrom(
                    typeof(Message)));
    }

    [Fact]
    public void Reaction_event_arguments_use_selected_nested_types()
    {
        var model = Load();

        var add =
            Assert.Single(
                model.Types,
                value =>
                    value.Name ==
                    "MessageReactionAddEventArgs");

        Assert.Equal(
            "MessageReactionEmoji",
            Assert.Single(
                    add.Members,
                    member =>
                        member.Name == "Emoji")
                .Type.Name);

        Assert.Equal(
            "ReactionType",
            Assert.Single(
                    add.Members,
                    member =>
                        member.Name == "Type")
                .Type.Name);

        Assert.Equal(
            (int)NetCord.ReactionType.Normal,
            (int)ReactionType.Normal);

        Assert.Equal(
            (int)NetCord.ReactionType.Burst,
            (int)ReactionType.Burst);
    }

    [Fact]
    public void Reply_is_selected_from_the_real_RestMessage_contract()
    {
        var model = Load();

        var restMessage =
            Assert.Single(
                model.Types,
                value =>
                    value.Name == "RestMessage");

        var reply =
            Assert.Single(
                restMessage.Methods);

        Assert.Equal(
            "NetCord.Rest.RestMessage.ReplyAsync",
            reply.CanonicalId);

        Assert.Equal(
            "message.reply",
            reply.HostName);

        Assert.True(reply.IsAsync);

        Assert.Equal(
            "RestMessage",
            reply.Result.Name);

        var parameter =
            Assert.Single(
                reply.Parameters);

        Assert.Equal(
            "replyMessage",
            parameter.Name);

        Assert.Equal(
            "ReplyMessageProperties",
            parameter.Type.Name);
    }

    [Fact]
    public void Invented_NetCord_members_are_rejected()
    {
        var source =
            File.ReadAllText(
                Path.Combine(
                    Root,
                    "api",
                    "rune-api.yaml"));

        var invalid =
            source.Replace(
                "      - name: Username",
                "      - name: InventedUsername",
                StringComparison.Ordinal);

        var exception =
            Assert.Throws<RuneApiValidationException>(
                () =>
                    RuneApiLoader.LoadText(
                        invalid));

        Assert.Contains(
            "InventedUsername",
            exception.Message);

        Assert.Contains(
            "NetCord.User",
            exception.Message);
    }

    [Fact]
    public void Contract_fingerprint_is_deterministic()
    {
        var first = Load();
        var second = Load();

        Assert.Equal(
            first.Fingerprint,
            second.Fingerprint);

        Assert.Equal(
            64,
            first.Fingerprint.Length);
    }

    private static RuneApiModel Load() =>
        RuneApiLoader.Load(
            Path.Combine(
                Root,
                "api",
                "rune-api.yaml"));

    private static string FindRepositoryRoot()
    {
        var directory =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "Rune.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Rune repository root was not found.");
    }
}
