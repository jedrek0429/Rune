namespace Rune.Runtime.Exceptions;


public sealed class RuneCompilationException(
    string message,
    Exception? innerException = null)
    : Exception(message, innerException);

public sealed class RuneExecutionException(
    string message,
    Exception? innerException = null)
    : Exception(message, innerException);

public sealed class RuneTimeoutException(
    TimeSpan timeout)
    : Exception(
        $"Execution exceeded the {timeout.TotalSeconds:g} second limit.");
