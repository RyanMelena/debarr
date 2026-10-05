using FluentResults;
using Quartz;
using Wolverine.Persistence.EventSourcing;

namespace Debarr.Scanning;

/// <param name="VideoExtensions">The extensions as typed, separated by spaces or commas.</param>
/// <param name="ScanIntervalHours">Null switches the scheduled scan off.</param>
public sealed record ChangeLibrarySettings(string VideoExtensions, int? ScanIntervalHours, bool WatchFolders)
{
    /// <summary>The library's stream, which Wolverine loads the aggregate from.</summary>
    public Guid LibraryId => Library.StreamId;
}

public static partial class ChangeLibrarySettingsHandler
{
    /// <summary>Refuses no video extensions and a scan interval under 1 hour, each beneath its field.</summary>
    public static Result<LibrarySettings> Validate(ChangeLibrarySettings command, Library? library)
    {
        var videoExtensions = Scanning.VideoExtensions.Parse(command.VideoExtensions);
        var scanInterval = ScanInterval.Create(command.ScanIntervalHours);
        var merged = Result.Merge(videoExtensions.ToResult(), scanInterval.ToResult());
        return merged.IsFailed
            ? Result.Fail<LibrarySettings>(merged.Errors)
            : new LibrarySettings(videoExtensions.Value, scanInterval.Value, command.WatchFolders);
    }

    public static LibrarySettingsChanged Handle(ChangeLibrarySettings command, [WriteModel(Required = false)] Library? library, LibrarySettings settings) => new(settings);

    /// <summary>Schedules the library scan again when the scan interval changed, so the new interval counts from the save.</summary>
    /// <param name="library">The library as the handler decided from, before the commit.</param>
    public static async Task AfterCommitAsync(
        ChangeLibrarySettings command,
        [WriteModel(Required = false)] Library? library,
        LibrarySettings settings,
        ISchedulerFactory schedulerFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (settings.ScanInterval == (library ?? Library.Default).Settings.ScanInterval)
        {
            return;
        }

        try
        {
            await LibraryScanJob.ScheduleAsync(await schedulerFactory.GetScheduler(cancellationToken), settings.ScanInterval, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogScheduleFailed(logger, exception);
        }
    }

    [LoggerMessage(LogLevel.Error, "Could not schedule the library scan at the new scan interval.")]
    private static partial void LogScheduleFailed(ILogger logger, Exception exception);
}
