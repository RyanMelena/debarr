using Debarr.Activity;
using Debarr.Detecting;
using Microsoft.AspNetCore.Components;

namespace Debarr.Components.Pages;

/// <summary>
/// The video file page's Detect Now, which follows every file's running detections:
/// it reads Detecting while the video file's detection runs, and is unavailable while the video file has no file path or Detect Now runs on another file.
/// </summary>
public partial class DetectNowButton(DetectionOrchestrator detectionOrchestrator)
{
    private IReadOnlyList<RunningDetection> _running = [];

    [Parameter, EditorRequired]
    public FileHash VideoFile { get; set; }

    [Parameter]
    public bool HasFilePath { get; set; }

    /// <summary>Starts Detect Now on the video file.</summary>
    [Parameter]
    public EventCallback OnClick { get; set; }

    private bool IsRunning => _running.Any(running => running.VideoFile == VideoFile);

    /// <summary>Why Detect Now is unavailable; null when it is available.</summary>
    private string? UnavailableReason => HasFilePath switch
    {
        false => "No file path to read",
        _ when _running.Any(running => running.Origin == DetectionOrigin.DetectNow) => "Detect Now is running on another file",
        _ => null,
    };

    protected override IReadOnlySet<string> ReadModels { get; } = new HashSet<string>();

    protected override bool ShowsActivity(ActivityEvent activityEvent) => DetectionOrchestrator.ChangesRunningDetections(activityEvent);

    protected override Task ReloadAsync(CancellationToken cancellationToken)
    {
        _running = detectionOrchestrator.RunningDetections;
        return Task.CompletedTask;
    }

    private async Task DetectNowAsync()
    {
        await OnClick.InvokeAsync();
        _running = detectionOrchestrator.RunningDetections;
    }
}
