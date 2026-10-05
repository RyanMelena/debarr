using Debarr.Activity;
using Debarr.Detecting;
using Debarr.Extensions;
using Debarr.Scanning;
using Fisher;
using Microsoft.AspNetCore.Components;

namespace Debarr.Components.Layout;

/// <summary>
/// The library scan, the root folder removal and the detections running now: a list for the navigation, or one line for the app bar.
/// The list animates each piece of work as it starts and ends, and the line renders nothing while no work runs.
/// </summary>
public partial class RunningWork(IDocumentStore store, LibraryScanner libraryScanner, RootFolderRemover rootFolderRemover, DetectionOrchestrator detectionOrchestrator)
{
    private DateTimeOffset? _libraryScanStartedAt;
    private bool _libraryScanStopping;
    private RootFolderRemovalStartedEvent? _rootFolderRemoval;
    private int _detectionCount;
    private IReadOnlyList<Work> _work = [];

    /// <summary>Whether to show one line that opens the navigation, for the app bar while the navigation is closed.</summary>
    [Parameter]
    public bool Compact { get; set; }

    /// <summary>Called when the operator clicks the compact line.</summary>
    [Parameter]
    public EventCallback OnClick { get; set; }

    private string LibraryScanText => _libraryScanStopping ? "Stopping the library scan" : "Scanning the library";

    private string Summary
    {
        get
        {
            List<(string Text, string ShortText)> workKinds = [];
            if (_libraryScanStartedAt is not null)
            {
                workKinds.Add((LibraryScanText, _libraryScanStopping ? "stopping the scan" : "scanning"));
            }

            if (_rootFolderRemoval is not null)
            {
                workKinds.Add(("Removing a root folder", "removing a root folder"));
            }

            if (_detectionCount > 0)
            {
                var files = _detectionCount.ToCountText("file", "files");
                workKinds.Add(($"Detecting {files}", $"detecting {files}"));
            }

            if (workKinds is [var only])
            {
                return only.Text;
            }

            var summary = string.Join(", ", workKinds.Select(workKind => workKind.ShortText));
            return summary is [var first, .. var rest] ? $"{char.ToUpperInvariant(first)}{rest}" : summary;
        }
    }

    protected override IReadOnlySet<string> ReadModels { get; } = new HashSet<string> { LibraryScanSummaryRowProjection.ReadModel };

    protected override bool ShowsActivity(ActivityEvent activityEvent) =>
        LibraryScanner.StopsLibraryScan(activityEvent)
            || RootFolderRemover.ChangesRunningRootFolderRemoval(activityEvent)
            || DetectionOrchestrator.ChangesRunningDetections(activityEvent);

    /// <summary>Reads the open library scan and whether a scan pause is stopping it, the running root folder removal and the running detections.</summary>
    protected override async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await using (var session = store.QuerySession())
        {
            _libraryScanStartedAt = (await session.ReadOpenLibraryScanAsync(cancellationToken))?.StartedAt;
        }

        _libraryScanStopping = libraryScanner.IsStoppingScan;
        _rootFolderRemoval = rootFolderRemover.RunningRootFolderRemoval;
        var detections = detectionOrchestrator.RunningDetections;
        _detectionCount = detections.Count;

        List<Work> work = [];
        if (_libraryScanStartedAt is { } startedAt)
        {
            work.Add(new Work("library-scan", "running-work-library-scan", LibraryScanText, "system/tasks", null, startedAt));
        }

        if (_rootFolderRemoval is { } removal)
        {
            work.Add(new Work(
                $"root-folder-removal-{removal.RootFolder}",
                "running-work-root-folder-removal",
                $"Removing {removal.RootFolder}",
                "settings/library",
                removal.RootFolder,
                removal.StartedAt));
        }

        if (detections is [var detection])
        {
            work.Add(new Work(
                $"detection-{detection.VideoFile}",
                "running-work-detections",
                $"Detecting {Path.GetFileName(detection.Path.Value)}",
                $"video-file/{detection.VideoFile}",
                detection.Path.Value,
                detection.StartedAt));
        }
        else if (detections.Count > 1)
        {
            work.Add(new Work("detections", "running-work-detections", $"Detecting {detections.Count.ToCountText("file", "files")}", "system/tasks", null, null));
        }

        _work = work;
    }

    /// <summary>One line of running work, a link to where it shows in full, with the full path on hover for one file.</summary>
    /// <param name="Key">Names the work across reloads, so a detection that ends leaves as the next one enters.</param>
    private sealed record Work(string Key, string Id, string Text, string Href, string? Title, DateTimeOffset? StartedAt);
}
