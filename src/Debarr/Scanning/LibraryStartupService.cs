using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Extensions;
using Fisher;
using Fisher.Linq;
using Wolverine.Runtime;

namespace Debarr.Scanning;

/// <summary>
/// Starts the library when the app starts: it interrupts every library scan the process stopped before its end,
/// then schedules the recurring library scan and runs a library scan immediately when the library has an enabled root folder or a video file.
/// </summary>
public sealed partial class LibraryStartupService(
    IDocumentStore store,
    IWolverineRuntime runtime,
    TimeProvider timeProvider,
    Quartz.ISchedulerFactory schedulerFactory,
    ILogger<LibraryStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Library library;
        IReadOnlyList<Guid> openLibraryScanIds;
        bool hasVideoFile;
        await using (var session = store.QuerySession())
        {
            library = await Library.ReadAsync(session, cancellationToken);
            openLibraryScanIds = [.. (await session.OpenLibraryScans().ToListAsync(cancellationToken)).Select(row => row.Id)];
            hasVideoFile = await session.AnyMediaRowAsync(cancellationToken);
        }

        var noticedAt = timeProvider.GetUtcNow();
        foreach (var libraryScanId in openLibraryScanIds)
        {
            var result = await runtime.SendCommandAsync(new InterruptLibraryScan(libraryScanId, noticedAt), cancellationToken);
            if (result.IsFailed)
            {
                LogInterruptFailed(libraryScanId, result.GetFormError());
            }
        }

        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        await LibraryScanJob.ScheduleAsync(scheduler, library.Settings.ScanInterval, cancellationToken);

        // With no enabled root folder and no video file, a library scan has nothing to read, remove or archive.
        if (library.RootFolders.Any(rootFolder => rootFolder.Enabled) || hasVideoFile)
        {
            await LibraryScanJob.TriggerNowAsync(scheduler, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(LogLevel.Error, "Could not interrupt the library scan {LibraryScanId}. {Error}")]
    private partial void LogInterruptFailed(Guid libraryScanId, string? error);
}
