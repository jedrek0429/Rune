using System.Diagnostics;
using System.Runtime.CompilerServices;
using Rune.Runtime.Exceptions;

namespace Rune.Runtime.Sandbox.Local;

public sealed class LocalSandbox(
    Process process) : ISandbox
{
    private int _disposed;

    public bool IsAlive =>
        Volatile.Read(ref _disposed) == 0 &&
        !process.HasExited;

    public async ValueTask SendAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        if (!IsAlive)
            throw new SandboxCommunicationException(
                "Sandbox process has exited.");

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await process.StandardInput.WriteLineAsync(message);
            await process.StandardInput.FlushAsync(cancellationToken);
        }
        catch (IOException exception)
        {
            throw new SandboxCommunicationException(
                "Failed to write to sandbox.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new SandboxCommunicationException(
                "Sandbox input is no longer available.",
                exception);
        }
    }

    public async IAsyncEnumerable<string> ReadAsync(
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;

            try
            {
                line = await process.StandardOutput.ReadLineAsync(
                    cancellationToken);
            }
            catch (IOException exception)
            {
                throw new SandboxCommunicationException(
                    "Failed to read from sandbox.",
                    exception);
            }

            if (line is null)
                yield break;

            yield return line;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (process.HasExited)
        {
            process.Dispose();
            return;
        }

        try
        {
            await process.StandardInput.WriteLineAsync(
                """{"type":"shutdown"}""");

            await process.StandardInput.FlushAsync();
        }
        catch (IOException)
        {
            // Process already died. Nothing to shut down gracefully.
        }
        catch (InvalidOperationException)
        {
            // stdin already closed.
        }

        try
        {
            process.StandardInput.Close();
        }
        catch
        {
            // Nothing useful to do here.
        }

        using var timeout =
            new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            await process.WaitForExitAsync();
        }
        finally
        {
            process.Dispose();
        }
    }

    public ValueTask KillAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
