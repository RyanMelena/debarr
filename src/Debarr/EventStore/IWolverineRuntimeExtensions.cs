using System.Diagnostics;
using FluentResults;
using Wolverine;
using Wolverine.Runtime;

namespace Debarr.EventStore;

public static partial class IWolverineRuntimeExtensions
{
    private static readonly TimeSpan[] WaitsBeforeWriteConflictRetries =
    [
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
    ];

    /// <summary>
    /// Runs <paramref name="command"/> and returns its refusal or failure, or a success once its events commit, and logs which with how long it took, retries included.
    /// A command that meets a write conflict decides again on a fresh read after each of <see cref="WaitsBeforeWriteConflictRetries"/>, and logs each retry at Warning and its failure at Error once they run out.
    /// A command cancelled through <paramref name="cancellationToken"/> while it runs or waits to retry commits nothing, logs its cancellation at Debug and throws <see cref="OperationCanceledException"/>.
    /// A command replies only when it fails, and the page reads what changed from its read models.
    /// The sender waits for this result, which <see cref="IMessageBus.PublishAsync"/> never returns, so a refusal reaches the form that sent the command.
    /// </summary>
    public static async Task<Result> SendCommandAsync(this IWolverineRuntime runtime, object command, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var commandType = command.GetType();
        var logger = runtime.LoggerFactory.CreateLogger(commandType);
        var result = await RunRetryingWriteConflictsAsync(runtime, command, logger, cancellationToken);
        var durationMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        if (result.HasError<CommandCancelledError>())
        {
            if (command is IFilePathCommand cancelledFilePathCommand)
            {
                LogFilePathCancelled(logger, commandType.Name, cancelledFilePathCommand.Path.Value, durationMs);
            }
            else
            {
                LogCancelled(logger, commandType.Name, durationMs);
            }

            throw new OperationCanceledException(cancellationToken);
        }

        var error = string.Join(" ", result.Errors.Select(reason => reason.Message));
        var failureLevel = result.HasError<WriteConflictError>() ? LogLevel.Error : LogLevel.Information;
        switch (result.IsSuccess, command)
        {
            case (true, IFilePathCommand filePathCommand):
                LogFilePathSucceeded(logger, commandType.Name, filePathCommand.Path.Value, durationMs);
                break;
            case (true, _):
                LogSucceeded(logger, commandType.Name, durationMs);
                break;
            case (false, IFilePathCommand filePathCommand):
                LogFilePathFailed(logger, failureLevel, commandType.Name, filePathCommand.Path.Value, durationMs, error);
                break;
            case (false, _):
                LogFailed(logger, failureLevel, commandType.Name, durationMs, error);
                break;
        }

        return result;
    }

    private static async Task<Result> RunRetryingWriteConflictsAsync(IWolverineRuntime runtime, object command, ILogger logger, CancellationToken cancellationToken)
    {
        var result = await RunOnFreshReadAsync(runtime, command, cancellationToken);
        for (var attempt = 1; attempt <= WaitsBeforeWriteConflictRetries.Length && result.HasError<WriteConflictError>(out var writeConflicts); attempt++)
        {
            var conflict = writeConflicts.First().Exception.Message;
            if (command is IFilePathCommand filePathCommand)
            {
                LogFilePathWriteConflictRetry(logger, command.GetType().Name, filePathCommand.Path.Value, attempt, conflict);
            }
            else
            {
                LogWriteConflictRetry(logger, command.GetType().Name, attempt, conflict);
            }

            await Task.Delay(WaitsBeforeWriteConflictRetries[attempt - 1], cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (cancellationToken.IsCancellationRequested)
            {
                return Result.Fail(new CommandCancelledError());
            }

            result = await RunOnFreshReadAsync(runtime, command, cancellationToken);
        }

        return result;
    }

    private static async Task<Result> RunOnFreshReadAsync(IWolverineRuntime runtime, object command, CancellationToken cancellationToken) =>
        // SQLite waits out another writer's lock synchronously, so the command runs on the thread pool, off the sender's context, such as a page's renderer.
#pragma warning disable RS0030
        await Task.Run(() => new MessageBus(runtime).InvokeAsync<Result?>(command, cancellationToken), cancellationToken) ?? Result.Ok();
#pragma warning restore RS0030

    [LoggerMessage(LogLevel.Information, "{Command} succeeded in {DurationMs} ms.")]
    private static partial void LogSucceeded(ILogger logger, string command, long durationMs);

    [LoggerMessage("{Command} failed in {DurationMs} ms. {Error}")]
    private static partial void LogFailed(ILogger logger, LogLevel level, string command, long durationMs, string error);

    [LoggerMessage(LogLevel.Warning, "{Command} met a write conflict on attempt {Attempt} and decides again on a fresh read. {Error}")]
    private static partial void LogWriteConflictRetry(ILogger logger, string command, int attempt, string error);

    [LoggerMessage(LogLevel.Debug, "{Command} was cancelled after {DurationMs} ms.")]
    private static partial void LogCancelled(ILogger logger, string command, long durationMs);

    [LoggerMessage(LogLevel.Debug, "{Command} of {Path} was cancelled after {DurationMs} ms.")]
    private static partial void LogFilePathCancelled(ILogger logger, string command, string path, long durationMs);

    [LoggerMessage(LogLevel.Debug, "{Command} of {Path} succeeded in {DurationMs} ms.")]
    private static partial void LogFilePathSucceeded(ILogger logger, string command, string path, long durationMs);

    [LoggerMessage("{Command} of {Path} failed in {DurationMs} ms. {Error}")]
    private static partial void LogFilePathFailed(ILogger logger, LogLevel level, string command, string path, long durationMs, string error);

    [LoggerMessage(LogLevel.Warning, "{Command} of {Path} met a write conflict on attempt {Attempt} and decides again on a fresh read. {Error}")]
    private static partial void LogFilePathWriteConflictRetry(ILogger logger, string command, string path, int attempt, string error);
}
