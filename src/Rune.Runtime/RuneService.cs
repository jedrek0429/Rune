using Rune.Core.Runes;
using Rune.Runtime.Compilation;
using Rune.Runtime.Native;

namespace Rune.Runtime;

public sealed class RuneService(
    RuneRegistry runeRegistry,
    CompilerRegistry compilerRegistry,
    IRuneComponentRuntime runtime)
{
    public async ValueTask<RegisteredRune> RegisterAsync(
        ulong guildId,
        string name,
        RuneLanguage language,
        RuneEventType eventType,
        string source,
        CancellationToken cancellationToken = default)
    {
        if (runeRegistry.Get(guildId, name) is not null)
            throw new InvalidOperationException(
                $"A rune named '{name}' already exists.");

        var compiled = await compilerRegistry
            .Get(language)
            .CompileAsync(eventType, source, cancellationToken);

        if (compiled.EventType != eventType)
        {
            throw new InvalidOperationException(
                "The compiled Component world does not match the registered event.");
        }

        var rune = new RegisteredRune(
            Guid.NewGuid(),
            guildId,
            name,
            language,
            eventType,
            source,
            compiled.Wasm,
            true);

        runtime.LoadComponent(rune.Id, eventType, compiled.Wasm);

        if (runeRegistry.Add(rune))
            return rune;

        runtime.RemoveComponent(rune.Id);
        throw new InvalidOperationException(
            $"A rune named '{name}' already exists.");
    }

    public async ValueTask<RegisteredRune> UpdateAsync(
        RegisteredRune current,
        RuneLanguage language,
        string source,
        CancellationToken cancellationToken = default)
    {
        var compiled = await compilerRegistry
            .Get(language)
            .CompileAsync(current.EventType, source, cancellationToken);

        if (compiled.EventType != current.EventType)
        {
            throw new InvalidOperationException(
                "The compiled Component world does not match the registered event.");
        }

        var updated = current with
        {
            Language = language,
            Source = source,
            Wasm = compiled.Wasm
        };

        if (updated.Enabled)
        {
            // Native replacement validates before atomically swapping the Component.
            runtime.LoadComponent(updated.Id, updated.EventType, updated.Wasm);
        }

        runeRegistry.Replace(updated);
        return updated;
    }

    public ValueTask<RegisteredRune?> RemoveAsync(
        ulong guildId,
        string name)
    {
        var current = runeRegistry.Get(guildId, name);
        if (current is null)
            return ValueTask.FromResult<RegisteredRune?>(null);

        if (current.Enabled && !runtime.RemoveComponent(current.Id))
        {
            throw new InvalidOperationException(
                $"Rune '{current.Name}' is not loaded in the native runtime.");
        }

        if (!runeRegistry.Remove(guildId, name, out var removed))
        {
            throw new InvalidOperationException(
                $"Rune '{current.Name}' changed while it was being removed.");
        }

        return ValueTask.FromResult(removed);
    }

    public ValueTask<RegisteredRune?> SetEnabledAsync(
        ulong guildId,
        string name,
        bool enabled)
    {
        var current = runeRegistry.Get(guildId, name);
        if (current is null)
            return ValueTask.FromResult<RegisteredRune?>(null);
        if (current.Enabled == enabled)
            return ValueTask.FromResult<RegisteredRune?>(current);

        if (enabled)
        {
            runtime.LoadComponent(current.Id, current.EventType, current.Wasm);
        }
        else if (!runtime.RemoveComponent(current.Id))
        {
            throw new InvalidOperationException(
                $"Rune '{current.Name}' is not loaded in the native runtime.");
        }

        if (!runeRegistry.SetEnabled(guildId, name, enabled, out var updated))
        {
            if (enabled)
                runtime.RemoveComponent(current.Id);
            else
                runtime.LoadComponent(current.Id, current.EventType, current.Wasm);

            throw new InvalidOperationException(
                $"Rune '{current.Name}' changed while its state was being updated.");
        }

        return ValueTask.FromResult(updated);
    }
}
