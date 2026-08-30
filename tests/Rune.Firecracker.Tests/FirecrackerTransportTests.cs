using Rune.Core.Invocations;
using Rune.Runtime;

namespace Rune.Firecracker.Tests;

public sealed class FirecrackerTransportTests
{
    [Fact]
    public void MessageCreatePayloadPreservesSnowflakesAsDecimalStrings()
    {
        const ulong guildId = 18_446_744_073_709_551_610UL;
        const ulong channelId = 18_446_744_073_709_551_611UL;
        const ulong messageId = 18_446_744_073_709_551_612UL;
        const ulong authorId = 18_446_744_073_709_551_613UL;

        var invocation = new MessageCreateEventRuneInvocation(
            Guid.NewGuid(),
            guildId,
            channelId,
            messageId,
            authorId,
            "runner-test",
            "hello");

        var payload = RuneEventCodec.ToPayload(invocation);

        Assert.Equal(messageId.ToString(), payload.GetProperty("id").GetString());
        Assert.Equal(channelId.ToString(), payload.GetProperty("channelId").GetString());
        Assert.Equal(
            authorId.ToString(),
            payload.GetProperty("author").GetProperty("id").GetString());

        var decoded = Assert.IsType<MessageCreateEventRuneInvocation>(
            RuneEventCodec.FromPayload(
                invocation.InvocationId,
                guildId,
                Rune.Core.Runes.RuneEventType.MessageCreate,
                payload));

        Assert.Equal(channelId, decoded.ChannelId);
        Assert.Equal(messageId, decoded.MessageId);
        Assert.Equal(authorId, decoded.AuthorId);
        Assert.Equal("runner-test", decoded.AuthorUsername);
        Assert.Equal("hello", decoded.Content);
    }

    [Fact]
    public void InvocationStreamIsLanguageAgnostic()
    {
        var options = new RuneRedisOptions
        {
            InvocationStream = "test:invocations"
        };

        Assert.Equal("test:invocations", options.InvocationStream);
    }

    [Fact]
    public void ExecutionEnvelopesDoNotExposeSourceLanguage()
    {
        Assert.Null(typeof(RuneInvocationEnvelope).GetProperty("Language"));
        Assert.Null(typeof(RuneResultEnvelope).GetProperty("Language"));
    }
}
