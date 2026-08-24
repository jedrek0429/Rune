using System.Text.Json;

using Rune.Runtime;

namespace Rune.Runtime.Wasm;

internal sealed class RuneExecutionContext(
    Guid invocationId,
    RuneRuntimeOptions options)
{
    private readonly List<RuneHostRequest> _requests = [];

    public IReadOnlyList<RuneHostRequest> Requests =>
        _requests;

    public string? Error { get; private set; }

    public void Reply(string content)
    {
        if (Error is not null)
            return;

        if (_requests.Count >=
            options.MaxHostRequestsPerInvocation)
        {
            Error =
                "Rune exceeded the host request limit.";

            return;
        }

        if (content.Length > options.MaxReplyLength)
        {
            Error =
                $"Reply exceeds the {options.MaxReplyLength} character limit.";

            return;
        }

        _requests.Add(
            new RuneHostRequest(
                "request",
                invocationId,
                "message.reply",
                JsonSerializer.SerializeToElement(
                    new
                    {
                        content
                    })));
    }
}
