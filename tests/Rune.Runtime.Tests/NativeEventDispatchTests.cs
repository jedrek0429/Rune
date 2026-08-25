using Rune.Core.Invocations;
using Rune.Core.Runes;
using Rune.Runtime.Native;
using Xunit;

namespace Rune.Runtime.Tests;

public sealed class NativeEventDispatchTests
{
    [Fact]
    public async Task Dispatch_invokes_each_matching_rune_and_commits_native_actions()
    {
        var registry = new RuneRegistry();
        var first = Rune("first", RuneEventType.MessageCreate);
        var second = Rune("second", RuneEventType.MessageCreate);
        registry.Add(first);
        registry.Add(second);
        registry.Add(Rune("other-event", RuneEventType.MessageDelete));
        var runtime = new DispatchRuntime();
        runtime.Results[first.Id] = new RuneInvocationResult(
            [new RuneAction(RuneActionKind.Reply, "one")]);
        runtime.Results[second.Id] = new RuneInvocationResult(
            [new RuneAction(RuneActionKind.Reply, "two")]);
        var host = new RecordingHost();
        var dispatcher = new RuneEventDispatcher(registry, runtime, host);
        var invocation = MessageCreate();

        var failures = await dispatcher.DispatchAsync(invocation);

        Assert.Empty(failures);
        Assert.Equal([first.Id, second.Id], runtime.Invoked);
        Assert.Collection(
            host.Requests,
            request => AssertReply(request, invocation.InvocationId, "one"),
            request => AssertReply(request, invocation.InvocationId, "two"));
    }

    [Fact]
    public async Task Native_failure_is_isolated_to_its_rune()
    {
        var registry = new RuneRegistry();
        var failed = Rune("failed", RuneEventType.MessageCreate);
        var healthy = Rune("healthy", RuneEventType.MessageCreate);
        registry.Add(failed);
        registry.Add(healthy);
        var runtime = new DispatchRuntime();
        runtime.Failures[failed.Id] = new RuneNativeException("component trapped");
        runtime.Results[healthy.Id] = new RuneInvocationResult(
            [new RuneAction(RuneActionKind.Reply, "healthy")]);
        var host = new RecordingHost();
        var dispatcher = new RuneEventDispatcher(registry, runtime, host);

        var failures = await dispatcher.DispatchAsync(MessageCreate());

        Assert.Collection(
            failures,
            failure =>
            {
                Assert.Equal("failed", failure.RuneName);
                Assert.Contains("component trapped", failure.Message);
            });
        Assert.Single(host.Requests);
    }

    private static void AssertReply(
        RuneHostRequest request,
        Guid invocationId,
        string content)
    {
        Assert.Equal("host_request", request.Type);
        Assert.Equal(invocationId, request.InvocationId);
        Assert.Equal("message.reply", request.Method);
        Assert.Equal(content, request.Arguments.GetProperty("content").GetString());
    }

    private static RegisteredRune Rune(string name, RuneEventType eventType) =>
        new(
            Guid.NewGuid(),
            42,
            name,
            RuneLanguage.JavaScript,
            eventType,
            "source",
            [1],
            true);

    private static MessageCreateEventRuneInvocation MessageCreate() =>
        new(
            Guid.NewGuid(),
            42,
            10,
            20,
            30,
            "Ada",
            "hello");

    private sealed class DispatchRuntime : IRuneComponentRuntime
    {
        public Dictionary<Guid, RuneInvocationResult> Results { get; } = [];
        public Dictionary<Guid, Exception> Failures { get; } = [];
        public List<Guid> Invoked { get; } = [];

        public void LoadComponent(
            Guid runeId,
            RuneEventType eventType,
            ReadOnlySpan<byte> component) =>
            throw new NotSupportedException();

        public bool RemoveComponent(Guid runeId) => throw new NotSupportedException();

        public RuneInvocationResult Invoke(Guid runeId, EventRuneInvocation invocation)
        {
            Invoked.Add(runeId);
            if (Failures.TryGetValue(runeId, out var failure))
                throw failure;
            return Results[runeId];
        }
    }

    private sealed class RecordingHost : IRuneHostRequestHandler
    {
        public List<RuneHostRequest> Requests { get; } = [];

        public ValueTask HandleAsync(
            EventRuneInvocation invocation,
            RuneHostRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.CompletedTask;
        }
    }
}
