using Rune.Core.Runes;
using Rune.Runtime.Compilation;
using Rune.Runtime.Wasm;

namespace Rune.Runtime;

public sealed class RuneService(
    RuneRegistry runeRegistry,
    CompilerRegistry compilerRegistry,
    RuneExecutor executor)
{
    public async ValueTask<RegisteredRune> RegisterAsync(
        ulong guildId,
        string name,
        RuneLanguage language,
        string source,
        CancellationToken cancellationToken = default)
    {
        if (runeRegistry.Get(guildId, name) is not null)
            throw new InvalidOperationException(
                $"A rune named '{name}' already exists.");

        var compiler = compilerRegistry.Get(language);

        var compiled = await compiler.CompileAsync(
            source,
            cancellationToken);

        var rune = new RegisteredRune(
            Guid.NewGuid(),
            guildId,
            name,
            language,
            RuneEventType.MessageCreate,
            source,
            compiled.Wasm,
            true);

        if (!runeRegistry.Add(rune))
            throw new InvalidOperationException(
                $"A rune named '{name}' already exists.");

        return rune;
    }

    public async ValueTask<RegisteredRune> UpdateAsync(
        RegisteredRune current,
        RuneLanguage language,
        string source,
        CancellationToken cancellationToken = default)
    {
        var compiler = compilerRegistry.Get(language);

        var compiled = await compiler.CompileAsync(
            source,
            cancellationToken);

        var updated = current with
        {
            Language = language,
            Source = source,
            Wasm = compiled.Wasm
        };

        await executor.StopAsync(current.Id);

        runeRegistry.Replace(updated);

        if (updated.Enabled)
            executor.Resume(updated.Id);

        return updated;
    }

    public async ValueTask<RegisteredRune?> RemoveAsync(
        ulong guildId,
        string name)
    {
        if (!runeRegistry.Remove(
                guildId,
                name,
                out var rune))
        {
            return null;
        }

        await executor.StopAsync(rune!.Id);

        return rune;
    }

    public async ValueTask<RegisteredRune?> SetEnabledAsync(
        ulong guildId,
        string name,
        bool enabled)
    {
        if (!runeRegistry.SetEnabled(
                guildId,
                name,
                enabled,
                out var rune))
        {
            return null;
        }

        if (enabled)
            executor.Resume(rune!.Id);
        else
            await executor.StopAsync(rune!.Id);

        return rune;
    }
}
