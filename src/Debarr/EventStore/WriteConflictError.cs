using FluentResults;

namespace Debarr.EventStore;

/// <summary>The reply of a command whose write met a write conflict, which committed nothing.</summary>
public sealed class WriteConflictError(Exception exception) : ExceptionalError("Another change was saved at the same time, so nothing was saved. Try again.", exception);
