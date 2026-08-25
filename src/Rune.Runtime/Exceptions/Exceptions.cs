namespace Rune.Runtime.Exceptions;


public sealed class RuneCompilationException(
    string message,
    Exception? innerException = null)
    : Exception(message, innerException);
