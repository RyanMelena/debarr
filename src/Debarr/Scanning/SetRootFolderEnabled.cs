using FluentResults;
using Quartz;
using Wolverine.Persistence.EventSourcing;

namespace Debarr.Scanning;

public sealed record SetRootFolderEnabled(LocalPath Path, bool Enabled)
{
    /// <summary>The library's stream, which Wolverine loads the aggregate from.</summary>
    public Guid LibraryId => Library.StreamId;
}

public static partial class SetRootFolderEnabledHandler
{
    public static Result<RootFolder> Validate(SetRootFolderEnabled command, Library? library) =>
        library?.FindRootFolder(command.Path) is { } rootFolder ? rootFolder : Result.Fail($"{command.Path.Value} is no longer a root folder.");

    /// <summary>Enables or disables the root folder, and does nothing when it already is.</summary>
    public static IReadOnlyList<object> Handle(SetRootFolderEnabled command, [WriteModel(Required = false)] Library? library, RootFolder rootFolder) =>
        rootFolder.Enabled == command.Enabled
            ? []
            : [command.Enabled ? new RootFolderEnabled(command.Path) : new RootFolderDisabled(command.Path)];

    /// <summary>Runs a library scan once the root folder is enabled, and starts the removal of its file paths once it is disabled.</summary>
    /// <param name="rootFolder">The root folder as the handler decided from, before the commit.</param>
    public static async Task AfterCommitAsync(
        SetRootFolderEnabled command,
        RootFolder rootFolder,
        ISchedulerFactory schedulerFactory,
        RootFolderRemover rootFolderRemover,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (rootFolder.Enabled == command.Enabled)
        {
            return;
        }

        if (command.Enabled)
        {
            try
            {
                await LibraryScanJob.TriggerNowAsync(await schedulerFactory.GetScheduler(cancellationToken), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogScanFailed(logger, exception, command.Path.Value);
            }
        }
        else
        {
            try
            {
                await rootFolderRemover.EnqueueAsync(command.Path, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogRemovalFailed(logger, exception, command.Path.Value);
            }
        }
    }

    [LoggerMessage(LogLevel.Error, "Could not start a library scan after enabling {RootFolder}.")]
    private static partial void LogScanFailed(ILogger logger, Exception exception, string rootFolder);

    [LoggerMessage(LogLevel.Error, "Could not start the removal of the file paths under {RootFolder}.")]
    private static partial void LogRemovalFailed(ILogger logger, Exception exception, string rootFolder);
}
