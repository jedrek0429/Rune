using Rune.Api.Generator;
using Xunit;

namespace Rune.Api.Generator.Tests;

public sealed class ApiGenerationTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void Manifest_is_validated_against_pinned_NetCord_contracts()
    {
        var model = Load();

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
        Assert.Equal("RestMessage", message.Base);
        Assert.Empty(message.Members);

        var restMessage = Assert.Single(
            model.Types,
            value => value.Name == "RestMessage");
        Assert.Equal(
            ["Id", "ChannelId", "Content", "Author"],
            restMessage.Members.Select(value => value.Name));
    }

    [Fact]
    public void Selected_reply_method_is_the_real_inherited_NetCord_method()
    {
        var model = Load();
        var restMessage = Assert.Single(
            model.Types,
            value => value.Name == "RestMessage");
        var reply = Assert.Single(restMessage.Methods);

        Assert.Equal("ReplyAsync", reply.Name);
        Assert.Equal("NetCord.Rest.RestMessage.ReplyAsync", reply.CanonicalId);
        Assert.True(reply.IsAsync);
        Assert.Equal("RestMessage", reply.Result.Name);

        var parameter = Assert.Single(reply.Parameters);
        Assert.Equal("replyMessage", parameter.Name);
        Assert.Equal("ReplyMessageProperties", parameter.Type.Name);
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
    public void Invented_NetCord_method_is_rejected()
    {
        var source = File.ReadAllText(
            Path.Combine(Root, "api", "rune-api.yaml"));
        var invalid = source.Replace(
            "      - name: ReplyAsync",
            "      - name: FetchEverythingAsync",
            StringComparison.Ordinal);

        var exception = Assert.Throws<RuneApiValidationException>(
            () => RuneApiLoader.LoadText(invalid));

        Assert.Contains("FetchEverythingAsync", exception.Message);
        Assert.Contains("NetCord.Rest.RestMessage", exception.Message);
    }

    [Fact]
    public void Generator_emits_all_microvm_language_sdks_and_no_wit_contract()
    {
        var output = RuneApiEmitter.Emit(Load());

        Assert.Contains("generated/javascript/rune-api.js", output.Keys);
        Assert.Contains("generated/typescript/rune-api.ts", output.Keys);
        Assert.Contains("generated/python/rune_api.py", output.Keys);
        Assert.Contains("generated/ruby/rune_api.rb", output.Keys);
        Assert.Contains("generated/rust/rune_api.rs", output.Keys);
        Assert.Contains("generated/c/rune_api.h", output.Keys);
        Assert.Contains("generated/cpp/rune_api.hpp", output.Keys);
        Assert.Contains("generated/csharp/RuneApi.cs", output.Keys);
        Assert.DoesNotContain("wit/rune-api.wit", output.Keys);
    }

    [Fact]
    public void Generated_wrappers_project_the_same_reply_call_idiomatically()
    {
        var output = RuneApiEmitter.Emit(Load());
        const string canonical = "NetCord.Rest.RestMessage.ReplyAsync";

        Assert.Contains(canonical, output["generated/javascript/rune-api.js"]);
        Assert.Contains("async reply(replyMessage)", output["generated/javascript/rune-api.js"]);
        Assert.Contains(canonical, output["generated/typescript/rune-api.ts"]);
        Assert.Contains("async reply(", output["generated/typescript/rune-api.ts"]);
        Assert.Contains(canonical, output["generated/python/rune_api.py"]);
        Assert.Contains("async def reply(", output["generated/python/rune_api.py"]);
        Assert.Contains(canonical, output["generated/ruby/rune_api.rb"]);
        Assert.Contains("def reply(", output["generated/ruby/rune_api.rb"]);
        Assert.Contains(canonical, output["generated/rust/rune_api.rs"]);
        Assert.Contains("rune_rest_message_reply", output["generated/c/rune_api.h"]);
        Assert.Contains("reply(RpcTransport&", output["generated/cpp/rune_api.hpp"]);
        Assert.Contains("ReplyAsync(", output["generated/csharp/RuneApi.cs"]);
    }

    [Fact]
    public void Rpc_manifest_contains_only_explicitly_selected_host_calls()
    {
        var output = RuneApiEmitter.Emit(Load());
        var rpc = output["generated/api/rpc.json"];

        Assert.Contains("NetCord.Rest.RestMessage.ReplyAsync", rpc);
        Assert.DoesNotContain("GetChannel", rpc, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetGuild", rpc, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GatewayClient", rpc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Documentation_is_generated_for_every_supported_language()
    {
        var output = RuneApiEmitter.Emit(Load());

        foreach (var language in new[]
                 {
                     "javascript", "typescript", "python", "ruby",
                     "rust", "c", "cpp", "csharp"
                 })
        {
            var docs = output[$"docs/api/generated/{language}.md"];
            Assert.Contains("MessageCreate", docs);
            Assert.Contains("Reply", docs, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("does not expose fetching", docs);
        }
    }

    [Fact]
    public void Generation_is_deterministic()
    {
        var model = Load();
        var first = RuneApiEmitter.Emit(model);
        var second = RuneApiEmitter.Emit(model);

        Assert.Equal(first.Keys, second.Keys);
        foreach (var path in first.Keys)
            Assert.Equal(first[path], second[path]);
    }

    [Fact]
    public void Selected_object_properties_resolve_to_selected_Rune_API_types()
    {
        var model = Load();
        var restMessage = Assert.Single(
            model.Types,
            value => value.Name == "RestMessage");
        var author = Assert.Single(
            restMessage.Members,
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

    private static RuneApiModel Load() =>
        RuneApiLoader.Load(Path.Combine(Root, "api", "rune-api.yaml"));

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
