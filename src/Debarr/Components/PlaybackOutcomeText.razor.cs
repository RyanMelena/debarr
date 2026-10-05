using Debarr.Detecting;
using Debarr.Playing;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Debarr.Components;

/// <summary>What a playback sent and where the ratio came from, such as "2.40 detected", or why it sent nothing.</summary>
public partial class PlaybackOutcomeText
{
    [Parameter, EditorRequired]
    public PlaybackOutcome Outcome { get; set; } = default!;

    /// <summary>Why a playback sent nothing, as the UI words it, such as "a stream".</summary>
    private static string NotSentText(PlaybackOutcome outcome) => outcome switch
    {
        PlaybackOutcome.Stream => "a stream",
        PlaybackOutcome.DontSend => "the override says don't send",
        PlaybackOutcome.NoPlayerAspectRatio => "the player reported no ratio",
        PlaybackOutcome.Failed => "handling failed",
        _ => "",
    };

    /// <summary>Where a sent ratio came from, in the colour and icon of the status it came from, worded after the ratio, such as "from file".</summary>
    private static StateDisplay DisplayOf(NotificationAspectRatioSource source) => source switch
    {
        NotificationAspectRatioSource.Detected => VideoFileStatusText.DisplayOf(VideoFileStatus.Detected) with { Text = "detected" },
        NotificationAspectRatioSource.Container => VideoFileStatusText.DisplayOf(VideoFileStatus.FromFile) with { Text = "from file" },
        NotificationAspectRatioSource.Manual => VideoFileStatusText.DisplayOf(VideoFileStatus.Manual) with { Text = "manual" },
        NotificationAspectRatioSource.Player => new("from the player", Icons.Material.Outlined.Cast, Color.Info),
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };
}
