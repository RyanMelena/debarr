using FluentResults;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Util;

namespace Debarr.EventStore;

/// <summary>The failed <see cref="Result"/> a command replies with when its handler refuses it or it throws, which the form that sent it shows.</summary>
public static class CommandReply
{
    /// <summary>Replies with the errors of <paramref name="validated"/> when it failed; true when the command stops.</summary>
    public static async Task<bool> RefuseAsync(MessageContext context, ResultBase validated)
    {
        if (validated.IsSuccess)
        {
            return false;
        }

        await FailAsync(context, Result.Fail(validated.Errors));
        return true;
    }

    /// <summary>Replies to a command its sender cancelled while it ran.</summary>
    public static async Task CancelAsync(MessageContext context) => await FailAsync(context, Result.Fail(new CommandCancelledError()));

    /// <summary>Replies to a command that threw with a form error that says nothing was saved and points to the log, where the executor logged the exception.</summary>
    public static async ValueTask FailAsync(IWolverineRuntime runtime, IEnvelopeLifecycle lifecycle, Exception exception) =>
        await FailAsync(lifecycle, Result.Fail(new ExceptionalError($"Nothing was saved. {exception.Message} System > Logs has the details.", exception)));

    public static async Task FailOnWriteConflictAsync(MessageContext context, Exception exception) => await FailAsync(context, Result.Fail(new WriteConflictError(exception)));

    /// <summary>Replies with <paramref name="failure"/> when the sender waits for a <see cref="Result"/>.</summary>
    private static async Task FailAsync(IEnvelopeLifecycle lifecycle, Result failure)
    {
        if (lifecycle is MessageContext context && lifecycle.Envelope?.ReplyRequested == typeof(Result).ToMessageTypeName())
        {
            await context.EnqueueCascadingAsync(failure);
        }
    }
}
