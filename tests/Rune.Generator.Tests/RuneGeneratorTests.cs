using Rune.Generator;

using Xunit;

namespace Rune.Generator.Tests;

public sealed class RuneGeneratorTests
{
    private static readonly string Root =
        FindRepositoryRoot();

    [Fact]
    public void Api_generation_emits_internal_and_guest_language_projections()
    {
        var output =
            RuneApiEmitter.Emit(LoadApi());

        Assert.Contains(
            "src/Rune.Api/Generated/RuneApi.g.cs",
            output.Keys);

        Assert.Contains(
            "generated/rust/rune_api.rs",
            output.Keys);

        Assert.Contains(
            "generated/typescript/rune-api.ts",
            output.Keys);
    }

    [Fact]
    public void Generated_CSharp_owns_projection_wire_shape_and_event_identity()
    {
        var csharp =
            RuneApiEmitter.Emit(LoadApi())[
                "src/Rune.Api/Generated/RuneApi.g.cs"];

        Assert.Contains(
            "public sealed record Message(",
            csharp);

        Assert.Contains(
            ": RestMessage(",
            csharp);

        Assert.Contains(
            "public static class NetCordRuneApi",
            csharp);

        Assert.Contains(
            "public static class RuneApiPayload",
            csharp);

        Assert.Contains(
            "public enum RuneApiEventType",
            csharp);

        Assert.Contains(
            "MessageReactionRemove,",
            csharp);

        Assert.Contains(
            "guildId = value.GuildId is ulong guildIdSnowflake ? Snowflake(guildIdSnowflake) : null",
            csharp);
    }

    [Fact]
    public void TypeScript_preserves_inheritance_and_event_argument_types()
    {
        var typescript =
            RuneApiEmitter.Emit(LoadApi())[
                "generated/typescript/rune-api.ts"];

        Assert.Contains(
            "export class Message extends RestMessage",
            typescript);

        Assert.Contains(
            "readonly channelId: string",
            typescript);

        Assert.Contains(
            "MessageReactionAdd: MessageReactionAddEventArgs",
            typescript);

        Assert.Contains(
            "async reply(replyMessage: ReplyMessageProperties)",
            typescript);
    }

    [Fact]
    public void Rust_wrapper_is_idiomatic()
    {
        var rust =
            RuneApiEmitter.Emit(LoadApi())[
                "generated/rust/rune_api.rs"];

        Assert.Contains(
            "pub struct Message",
            rust);

        Assert.Contains(
            "pub channel_id: u64",
            rust);

        Assert.Contains(
            "pub type_: ReactionType",
            rust);

        Assert.Contains(
            "pub fn reply(",
            rust);
    }

    [Fact]
    public void Runtime_protocol_is_generated_for_CSharp_Rust_and_guest()
    {
        var api = LoadApi();
        var runtime =
            RuntimeContractLoader.Load(
                Path.Combine(
                    Root,
                    "contracts",
                    "runtime.yaml"));

        var output =
            RuntimeContractEmitter.Emit(
                runtime,
                api);

        Assert.Equal(
            [
                "src/Rune.Firecracker.Guest/src/generated_runtime.rs",
                "src/Rune.Firecracker.Runner/src/generated_protocol.rs",
                "src/Rune.Firecracker/generated-runtime.sh",
                "src/Rune.Runtime/Generated/RuntimeProtocol.g.cs"
            ],
            output.Keys);

        var csharp =
            output[
                "src/Rune.Runtime/Generated/RuntimeProtocol.g.cs"];

        var rust =
            output[
                "src/Rune.Firecracker.Runner/src/generated_protocol.rs"];

        var shell =
            output[
                "src/Rune.Firecracker/generated-runtime.sh"];

        Assert.Contains(
            "public const string InvocationStream = \"rune:invocations\";",
            csharp);

        Assert.Contains(
            "public sealed record RuneInvocationEnvelope(",
            csharp);

        Assert.Contains(
            "pub const INVOCATION_STREAM: &str = \"rune:invocations\";",
            rust);

        Assert.Contains(
            "pub const MAX_INVOCATION_BYTES: usize = 131072;",
            rust);

        Assert.Contains(
            "RUNE_MAX_RESULT_BYTES=262144",
            shell);

        foreach (var runeEvent in api.Events)
        {
            Assert.Contains(
                $"    {runeEvent.Name},",
                rust);
        }
    }

    [Fact]
    public void Generation_is_deterministic()
    {
        var api = LoadApi();
        var runtime =
            RuntimeContractLoader.Load(
                Path.Combine(
                    Root,
                    "contracts",
                    "runtime.yaml"));

        AssertEqual(
            RuneApiEmitter.Emit(api),
            RuneApiEmitter.Emit(api));

        AssertEqual(
            RuntimeContractEmitter.Emit(
                runtime,
                api),
            RuntimeContractEmitter.Emit(
                runtime,
                api));
    }

    private static RuneApiModel LoadApi() =>
        RuneApiLoader.Load(
            Path.Combine(
                Root,
                "contracts",
                "rune-api.yaml"));

    private static void AssertEqual(
        IReadOnlyDictionary<string, string> first,
        IReadOnlyDictionary<string, string> second)
    {
        Assert.Equal(
            first.Keys,
            second.Keys);

        foreach (var path in first.Keys)
        {
            Assert.Equal(
                first[path],
                second[path]);
        }
    }

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

            directory =
                directory.Parent;
        }

        throw new InvalidOperationException(
            "Rune repository root was not found.");
    }
}
