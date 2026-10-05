namespace Debarr.Appearance;

public sealed record UISettingsChanged(UITheme Theme, DateTimeFormats Formats, bool ShowRelativeDates);
