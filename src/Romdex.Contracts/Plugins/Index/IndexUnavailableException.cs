namespace Romdex.Contracts.Plugins.Index;

/// <summary>
/// Thrown by an index plugin when its source can't answer right now: overloaded, rate-limiting, down or unreachable.
/// The core treats it as "try again later", not as "not found", and keeps the catalogue it already has.
/// </summary>
public sealed class IndexUnavailableException(string message, Exception? innerException = null) : Exception(message, innerException);
