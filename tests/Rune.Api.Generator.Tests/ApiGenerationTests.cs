using Rune.Api.Generator;
using Xunit;

namespace Rune.Api.Generator.Tests;

public sealed class ApiGenerationTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void Manifest_is_validated_against_pinned_NetCord_contracts()
    {
        var model = RuneApiLoader.Load(
            Path.Combine(Root, "api", "rune-api.yaml"));

        Assert.Equal("1.0.0-beta.16", model.NetCordVersion);
        Assert.Equal(
            [
                "MessageCreate",
                "MessageDelete",
                "MessageReactionAdd",
                "MessageReactionRemove"
            ],
            model.Events.Select(value => value.Name));

        var message = Assert.Single(
            model.Types,
            value => value.Name == "Message");

        Assert.Equal("NetCord.Gateway.Message", message.NetCordName);
        Assert.Equal(
            ["Id", "ChannelId", "Content", "Author"],
            message.Members.Select(value => value.Name));
    }

    [Fact]
    public void Invented_NetCord_member_is_rejected()
    {
        var source = File.ReadAllText(
            Path.Combine(Root, "api", "rune-api.yaml"));
        var invalid = source.Replace(
            "      - name: Username",
            "      - name: InventedUsername",
            StringComparison.Ordinal);

        var exception = Assert.Throws<RuneApiValidationException>(
            () => RuneApiLoader.LoadText(invalid));

        Assert.Contains("InventedUsername", exception.Message);
        Assert.Contains("NetCord.User", exception.Message);
    }

    [Fact]
    public void Generator_emits_four_typed_worlds_from_one_model()
    {
        var model = RuneApiLoader.Load(
            Path.Combine(Root, "api", "rune-api.yaml"));
        var output = RuneApiEmitter.Emit(model);
        var wit = output["wit/rune-api.wit"];

        Assert.Contains("world message-create-rune", wit);
        Assert.Contains("export handle: func(message: message);", wit);
        Assert.Contains("world message-delete-rune", wit);
        Assert.Contains(
            "export handle: func(args: message-delete-event-args);",
            wit);
        Assert.Contains("world message-reaction-add-rune", wit);
        Assert.Contains("world message-reaction-remove-rune", wit);
    }

    [Fact]
    public void Every_language_projection_comes_from_the_same_members()
    {
        var model = RuneApiLoader.Load(
            Path.Combine(Root, "api", "rune-api.yaml"));
        var output = RuneApiEmitter.Emit(model);

        Assert.Contains(
            "channelId",
            output["generated/javascript/rune-api.js"]);
        Assert.Contains(
            "channel_id",
            output["generated/python/rune_api.py"]);
        Assert.Contains(
            "channel_id",
            output["generated/rust/rune_api.rs"]);

        foreach (var member in model.Types.Single(
                     value => value.Name == "MessageReactionAddEventArgs").Members)
        {
            Assert.Contains(
                member.CanonicalId,
                output["generated/api/model.json"]);
        }
    }

    [Fact]
    public void Generation_is_deterministic_and_matches_committed_outputs()
    {
        var model = RuneApiLoader.Load(
            Path.Combine(Root, "api", "rune-api.yaml"));
        var first = RuneApiEmitter.Emit(model);
        var second = RuneApiEmitter.Emit(model);

        Assert.Equal(first.Keys, second.Keys);

        foreach (var path in first.Keys)
        {
            Assert.Equal(first[path], second[path]);
            Assert.Equal(
                File.ReadAllText(Path.Combine(Root, path)),
                first[path]);
        }
    }

    [Fact]
    public void Selected_object_properties_resolve_to_selected_Rune_API_types()
    {
        var model = RuneApiLoader.Load(
            Path.Combine(Root, "api", "rune-api.yaml"));

        var message = Assert.Single(
            model.Types,
            value => value.Name == "Message");

        var author = Assert.Single(
            message.Members,
            value => value.Name == "Author");

        Assert.Equal("User", author.Type.Name);
        Assert.True(author.Type.IsSelectedType);

        var reaction = Assert.Single(
        model.Types,
        value => value.Name == "MessageReactionAddEventArgs");

        Assert.Equal(
            "MessageReactionEmoji",
            reaction.Members.Single(value => value.Name == "Emoji").Type.Name);

        Assert.Equal(
            "ReactionType",
            reaction.Members.Single(value => value.Name == "Type").Type.Name);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rune.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Rune repository root was not found.");
    }
}
