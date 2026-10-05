using Debarr.Appearance;

namespace Debarr.Components.Pages.Settings;

/// <summary>The UI settings as Settings &gt; UI edits them.</summary>
public sealed record UISettingsForm
{
    public UITheme Theme { get; set; }

    public string ShortDateFormat { get; set; } = "";

    public string LongDateFormat { get; set; } = "";

    public string TimeFormat { get; set; } = "";

    public bool ShowRelativeDates { get; set; }

    public static UISettingsForm FromSettings(UISettings settings) => new()
    {
        Theme = settings.Theme,
        ShortDateFormat = settings.Formats.ShortDateFormat,
        LongDateFormat = settings.Formats.LongDateFormat,
        TimeFormat = settings.Formats.TimeFormat,
        ShowRelativeDates = settings.ShowRelativeDates,
    };

    public SaveUISettings ToSaveUISettings() =>
        new(Theme, new DateTimeFormats(ShortDateFormat, LongDateFormat, TimeFormat), ShowRelativeDates);
}
