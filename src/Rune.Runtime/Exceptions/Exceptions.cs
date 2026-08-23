namespace Rune.Runtime.Exceptions;

public sealed class SandboxCommunicationException(
    string message,
    Exception? innerException = null)
    : Exception(message, innerException);

