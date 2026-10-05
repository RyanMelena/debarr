using Debarr.Activity;
using Debarr.Detecting;
using Debarr.Scanning;
using Fisher;
using Microsoft.AspNetCore.Components;

namespace Debarr.Components.Pages;

public partial class TasksPage(IDocumentStore store, LibraryScanner libraryScanner, RootFolderRemover rootFolderRemover, DetectionOrchestrator detectionOrchestrator)
{
    private const int NewestLibraryScanCount = 5;

    private bool _loaded;

    // Null while the scheduled scan is off.
    private int? _scanIntervalHours;
    private bool _hasEnabledRootFolder;
    private IReadOnlyList<LibraryScanSummaryRow> _libraryScanSummaries = [];
    private bool _libraryScanStopping;

    // Null while the scheduled scan is off.
    private DateTimeOffset? _nextScheduledLibraryScan;

    private IReadOnlyList<RootFolderRemovalStartedEvent> _rootFolderRemovals = [];

    private IReadOnlyList<RunningDetection> _runningDetections = [];

    private readonly PageAction _scanNow = new();

    [CascadingParameter]
    private DateTimeFormatter Formatter { get; set; } = default!;

    private LibraryScanSummaryRow? RunningLibraryScan => _libraryScanSummaries.FirstOrDefault(summary => !summary.Closed);

    /// <summary>The newest library scan that ended or was interrupted.</summary>
    private LibraryScanSummaryRow? LastLibraryScan => _libraryScanSummaries.FirstOrDefault(summary => summary.Closed);

    private string IntervalText => _scanIntervalHours switch
    {
        null => "Off",
        1 => "1 hour",
        var hours => $"{hours} hours",
    };

    protected override IReadOnlySet<string> ReadModels { get; } = new HashSet<string> { nameof(Library), LibraryScanSummaryRowProjection.ReadModel };

    protected override bool ShowsActivity(ActivityEvent activityEvent) =>
        LibraryScanner.ChangesNextScheduledScan(activityEvent)
            || LibraryScanner.StopsLibraryScan(activityEvent)
            || RootFolderRemover.ChangesRunningRootFolderRemoval(activityEvent)
            || DetectionOrchestrator.ChangesRunningDetections(activityEvent);

    protected override async Task ReloadAsync(CancellationToken cancellationToken)
    {
        _nextScheduledLibraryScan = await libraryScanner.ReadNextScheduledScanAsync(cancellationToken);
        await using var session = store.QuerySession();
        var library = await Library.ReadAsync(session, cancellationToken);
        _hasEnabledRootFolder = library.RootFolders.Any(root => root.Enabled);
        _libraryScanSummaries = await session.ReadNewestLibraryScansAsync(NewestLibraryScanCount, cancellationToken);
        _libraryScanStopping = libraryScanner.IsStoppingScan;
        _scanIntervalHours = library.Settings.ScanInterval.Hours;
        _rootFolderRemovals = rootFolderRemover.RunningRootFolderRemoval is { } removal ? [removal] : [];
        _runningDetections = detectionOrchestrator.RunningDetections;
        _loaded = true;
    }

    private Task ScanNowAsync() => _scanNow.RunAsync(
        () => libraryScanner.ScanNowAsync(CancellationToken.None),
        Logger,
        "The library scan did not start.");
}
