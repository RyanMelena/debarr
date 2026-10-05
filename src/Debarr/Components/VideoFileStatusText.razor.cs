using Debarr.Detecting;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Debarr.Components;

/// <summary>
/// A video file's status, in the colour and icon each status has on every page.
/// While a detection of the file runs, it reads Detecting, and the same label changes back when the detection ends.
/// </summary>
public partial class VideoFileStatusText
{
    [Parameter, EditorRequired]
    public VideoFileStatus Status { get; set; }

    /// <summary>The detection running on the file; null when none runs.</summary>
    [Parameter]
    public RunningDetection? Running { get; set; }

    /// <summary>Whether a tooltip explains the status; false where the page shows <see cref="Explain"/> beside it.</summary>
    [Parameter]
    public bool ShowsExplanation { get; set; } = true;

    /// <summary>What the status means and what a playback of the file sends.</summary>
    public static string Explain(VideoFileStatus status) => status switch
    {
        VideoFileStatus.Detected => "Measured from the picture. A playback sends this ratio.",
        VideoFileStatus.FromFile => "The ratio the file states, accepted without measuring the picture. A playback sends it.",
        VideoFileStatus.Manual => "An override sets what a playback sends.",
        VideoFileStatus.Failed => "The last detection failed. A playback sends the player's ratio.",
        _ => "Waiting for detection. A playback sends the player's ratio.",
    };

    /// <summary>The word, icon and colour each status has on every page.</summary>
    public static StateDisplay DisplayOf(VideoFileStatus status) => status switch
    {
        VideoFileStatus.Detected => new("Detected", Icons.Material.Outlined.CheckCircle, Color.Success),
        VideoFileStatus.FromFile => new("From File", Icons.Material.Outlined.Inventory2, Color.Info),
        VideoFileStatus.Manual => new("Manual", Icons.Material.Outlined.Edit, Color.Tertiary),
        VideoFileStatus.Failed => new("Failed", Icons.Material.Outlined.ErrorOutline, Color.Error),
        VideoFileStatus.Pending => new("Pending", Icons.Material.Outlined.HourglassEmpty, Color.Default),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    private StateDisplay Display => Running is null ? DisplayOf(Status) : new("Detecting", Icons.Material.Outlined.Sync, Color.Info);

    private string Explanation => Running switch
    {
        null => Explain(Status),
        { Origin: DetectionOrigin.DetectNow } => "Detect Now is measuring this file. Its status shows once it ends.",
        _ => "The queue is measuring this file. Its status shows once it ends.",
    };
}
