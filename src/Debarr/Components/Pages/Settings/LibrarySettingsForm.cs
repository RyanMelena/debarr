using Debarr.Scanning;

namespace Debarr.Components.Pages.Settings;

/// <summary>The library settings as Settings &gt; Library edits them, with a scan interval of zero for a switched-off schedule.</summary>
public sealed record LibrarySettingsForm
{
    public string VideoExtensions { get; set; } = "";

    /// <summary>Zero switches the scheduled scan off.</summary>
    public int ScanIntervalHours { get; set; }

    public bool WatchFolders { get; set; }

    public static LibrarySettingsForm FromSettings(LibrarySettings settings) => new()
    {
        VideoExtensions = settings.VideoExtensions.ToString(),
        ScanIntervalHours = settings.ScanInterval.Hours ?? 0,
        WatchFolders = settings.WatchFolders,
    };

    public ChangeLibrarySettings ToChangeLibrarySettings() =>
        new(VideoExtensions, ScanIntervalHours == 0 ? null : ScanIntervalHours, WatchFolders);
}
