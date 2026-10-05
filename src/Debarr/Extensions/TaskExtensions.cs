using FluentResults;

namespace Debarr.Extensions;

public static partial class TaskExtensions
{
    /// <summary>
    /// The task's outcome as a result, with an exception it throws logged and returned as its failure, so a form that awaits it keeps what the operator entered.
    /// A cancellation still throws.
    /// </summary>
    /// <param name="failure">What an exception means for the operator, as a sentence that leads its message, such as The settings were not saved.</param>
    public static async Task<Result> ToResultAsync(this Task task, ILogger logger, string failure)
    {
        try
        {
            await task;
            return Result.Ok();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Fail(exception, logger, failure);
        }
    }

    /// <inheritdoc cref="ToResultAsync(Task, ILogger, string)"/>
    public static async Task<Result> ToResultAsync<TResult>(this Task<TResult> task, ILogger logger, string failure)
        where TResult : ResultBase
    {
        try
        {
            var result = await task;
            return result.IsFailed ? Result.Fail(result.Errors) : Result.Ok();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Fail(exception, logger, failure);
        }
    }

    private static Result Fail(Exception exception, ILogger logger, string failure)
    {
        LogFailure(logger, exception, failure);
        return Result.Fail(new ExceptionalError($"{failure} {exception.Message} System > Logs has the details.", exception));
    }

    [LoggerMessage(LogLevel.Error, "{Failure}")]
    private static partial void LogFailure(ILogger logger, Exception exception, string failure);
}
