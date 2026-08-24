namespace Rune.Runtime.Native;

public enum RuneActionKind : uint
{
    Reply = 1,
}

public sealed record RuneAction(
    RuneActionKind Kind,
    string Content);

public sealed record RuneInvocationResult(
    IReadOnlyList<RuneAction> Actions);
