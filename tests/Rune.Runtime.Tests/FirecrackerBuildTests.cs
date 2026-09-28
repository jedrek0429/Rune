using Rune.Api;

using Xunit;

namespace Rune.Runtime.Tests;

public sealed class FirecrackerBuildTests
{
    [Theory]
    [InlineData(RuneLanguage.JavaScript, "scriptc", "javascript")]
    [InlineData(RuneLanguage.TypeScript, "scriptc", "typescript")]
    [InlineData(RuneLanguage.Python, "python", "python")]
    [InlineData(RuneLanguage.Rust, "rust", "rust")]
    [InlineData(RuneLanguage.C, "clang", "c")]
    [InlineData(RuneLanguage.Cpp, "clang", "cpp")]
    public void Every_supported_language_maps_to_a_build_target(
        RuneLanguage language,
        string expectedPool,
        string expectedLanguage)
    {
        var target =
            FirecrackerRuneBuilder.GetBuildTarget(
                language);

        Assert.Equal(
            expectedPool,
            target.Pool);

        Assert.Equal(
            expectedLanguage,
            target.Language);
    }

    [Fact]
    public void Source_composition_injects_generated_bindings_and_event_bootstrap()
    {
        var typescript =
            RuneSourceComposer.Compose(
                RuneLanguage.TypeScript,
                RuneApiEventType.MessageCreate,
                "await message.reply({ content: \"hello\" });",
                "class Message {}",
                "",
                "");

        Assert.Contains(
            "class Message {}",
            typescript);

        Assert.Contains(
            "async function rune(message: Message",
            typescript);

        Assert.Contains(
            "await message.reply",
            typescript);

        var javascript =
            RuneSourceComposer.Compose(
                RuneLanguage.JavaScript,
                RuneApiEventType.MessageCreate,
                "await message.reply({ content: \"hello\" });",
                "",
                "class Message {}",
                "");

        Assert.Contains(
            "class Message {}",
            javascript);

        Assert.Contains(
            "@param {Message} message",
            javascript);

        Assert.Contains(
            "await message.reply",
            javascript);

        Assert.DoesNotContain(
            "message: Message",
            javascript);

        var rust =
            RuneSourceComposer.Compose(
                RuneLanguage.Rust,
                RuneApiEventType.MessageCreate,
                "if message.content == \"!hello\" {}",
                "",
                "",
                "pub struct Message { pub content: String }");

        Assert.Contains(
            "pub struct Message",
            rust);

        Assert.Contains(
            "fn rune(message: Message) -> Result<(), String>",
            rust);

        Assert.DoesNotContain(
            "fn rune(message: Message, host:",
            rust);

        Assert.Contains(
            "if message.content == \"!hello\" {}",
            rust);
    }

    [Fact]
    public void Source_composition_leaves_unwrapped_languages_unchanged()
    {
        const string source = "print('hello')";

        Assert.Equal(
            source,
            RuneSourceComposer.Compose(
                RuneLanguage.Python,
                RuneApiEventType.MessageCreate,
                source,
                "",
                "",
                ""));
    }

    [Fact]
    public void Build_boundary_returns_executable_artifacts()
    {
        Assert.True(
            typeof(IRuneBuilder).IsInterface);

        Assert.Equal(
            "rune",
            new BuiltRuneArtifact(
                "id",
                "digest",
                "rune",
                1).Entrypoint);
    }
}
