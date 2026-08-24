using System.Collections.Concurrent;

using Extism.Sdk;

using Rune.Core.Runes;
using Rune.Runtime.Exceptions;

namespace Rune.Runtime.Wasm;

public sealed class RuneWasmCache(
    RuneRuntimeOptions options)
    : IDisposable
{
    private readonly ConcurrentDictionary<
        Guid,
        Lazy<Entry>> _entries = [];

    public Plugin Instantiate(
        RegisteredRune rune)
    {
        Lazy<Entry> lazy =
            _entries.GetOrAdd(
                rune.Id,
                _ => new Lazy<Entry>(
                    () => CreateEntry(rune.Wasm),
                    LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return lazy.Value
                .Compiled
                .Instantiate();
        }
        catch
        {
            _entries.TryRemove(
                rune.Id,
                out _);

            if (lazy.IsValueCreated)
                lazy.Value.Dispose();

            throw;
        }
    }

    public void Validate(
        byte[] wasm)
    {
        try
        {
            using var entry =
                CreateEntry(wasm);

            using var plugin =
                entry.Compiled.Instantiate();

            if (!plugin.FunctionExists("handle"))
            {
                throw new RuneCompilationException(
                    "Compiled rune does not export 'handle'.");
            }
        }
        catch (RuneCompilationException)
        {
            throw;
        }
        catch (ExtismException exception)
        {
            throw new RuneCompilationException(
                UserFacingError(
                    exception.Message),
                exception);
        }
    }

    public void Invalidate(
        Guid runeId)
    {
        if (!_entries.TryRemove(
                runeId,
                out var lazy))
        {
            return;
        }

        if (lazy.IsValueCreated)
            lazy.Value.Dispose();
    }

    public void Dispose()
    {
        foreach (var lazy in _entries.Values)
        {
            if (lazy.IsValueCreated)
                lazy.Value.Dispose();
        }

        _entries.Clear();
    }

    private Entry CreateEntry(
        byte[] wasm)
    {
        var functions =
            RuneHostFunctions.Create();

        try
        {
            var manifest =
                new Manifest(
                    new ByteArrayWasmSource(
                        wasm,
                        "main"))
                {
                    AllowedHosts = [],
                    AllowedPaths =
                        new Dictionary<string, string>(),

                    // CancellationToken enforces the exact
                    // Rune limit. This is a secondary hard stop.
                    Timeout =
                        options.ExecutionTimeout +
                        TimeSpan.FromMilliseconds(500),

                    MemoryOptions =
                        new MemoryOptions
                        {
                            MaxPages =
                                options.MaxMemoryPages
                        }
                };

            var initialisation =
                new PluginIntializationOptions
                {
                    WithWasi = false,
                    FuelLimit =
                        options.FuelLimit
                };

            var compiled =
                new CompiledPlugin(
                    manifest,
                    functions,
                    initialisation);

            return new Entry(
                compiled,
                functions);
        }
        catch
        {
            foreach (var function in functions)
                function.Dispose();

            throw;
        }
    }

    private static string UserFacingError(
        string message)
    {
        var line = message
            .Replace("\r\n", "\n")
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim();

        if (string.IsNullOrWhiteSpace(line))
            return "Generated WASM could not be loaded.";

        return line.Length <= 500
            ? line
            : line[..500] + "…";
    }

    private sealed class Entry(
        CompiledPlugin compiled,
        HostFunction[] functions)
        : IDisposable
    {
        public CompiledPlugin Compiled { get; } =
            compiled;

        public void Dispose()
        {
            Compiled.Dispose();

            foreach (var function in functions)
                function.Dispose();
        }
    }
}
