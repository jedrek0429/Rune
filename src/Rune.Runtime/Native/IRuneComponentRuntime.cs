using Rune.Core.Invocations;
using Rune.Core.Runes;

namespace Rune.Runtime.Native;

public interface IRuneComponentRuntime
{
    void LoadComponent(
        Guid runeId,
        RuneEventType eventType,
        ReadOnlySpan<byte> component);

    bool RemoveComponent(Guid runeId);

    RuneInvocationResult Invoke(
        Guid runeId,
        EventRuneInvocation invocation);
}
