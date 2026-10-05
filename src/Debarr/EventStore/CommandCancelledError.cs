using FluentResults;

namespace Debarr.EventStore;

/// <summary>The reply of a command its sender cancelled while it ran, which committed nothing.</summary>
public sealed class CommandCancelledError() : Error("The command was cancelled.");
