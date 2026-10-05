using System.Runtime.ExceptionServices;
using Debarr.Extensions;
using Wolverine;
using Wolverine.Runtime;

namespace Debarr.EventStore;

/// <summary>What every command's handler runs around its own work: the logging scope that names the command, and the reply to a command its sender cancels or that meets a write conflict.</summary>
public static class CommandMiddleware
{
    private static readonly Func<ILogger, string?, Guid, IDisposable?> CommandScope = LoggerMessage.DefineScope<string?, Guid>("{Command} {MessageId}");

    /// <summary>Opens the scope that names the command for everything logged while it runs, which ends when the handler returns.</summary>
    public static IDisposable? OpenScope(ILogger logger, Envelope envelope) => CommandScope(logger, envelope.Message?.GetType().Name, envelope.Id);

    /// <summary>
    /// Replies that the command was cancelled when its sender cancelled it, or that it met a write conflict, so the executor sees no failure to log.
    /// Both commit nothing.
    /// Any other exception reaches the executor, which logs it and fails the command.
    /// </summary>
    public static async Task OnExceptionAsync(Exception exception, MessageContext context, CancellationToken cancellation)
    {
        if (exception is OperationCanceledException && cancellation.IsCancellationRequested)
        {
            await CommandReply.CancelAsync(context);
        }
        else if (exception.IsWriteConflict())
        {
            await CommandReply.FailOnWriteConflictAsync(context, exception);
        }
        else
        {
            ExceptionDispatchInfo.Throw(exception);
        }
    }
}
