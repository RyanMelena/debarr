using System.Globalization;
using System.Text.RegularExpressions;
using Debarr.Appearance;

namespace Debarr.Components;

/// <summary>Shows moments in the server's time zone, in the formats the UI settings hold, with the invariant culture.</summary>
public sealed partial class DateTimeFormatter(UISettings settings, TimeProvider timeProvider)
{
    /// <summary>
    /// The short date and the time, such as "Sep 27 2026 14:05".
    /// With relative dates shown, a date today or yesterday is Today or Yesterday, a date in the six days before is its weekday,
    /// and any other date in the current year leaves out the year, such as "Today 14:05", "Wednesday 09:12" or "Mar 5 17:30".
    /// </summary>
    /// <param name="withSeconds">Whether the time shows seconds, such as "14:05:07", for a log entry.</param>
    public string FormatDateTime(DateTimeOffset value, bool withSeconds = false)
    {
        var local = ToLocal(value);
        return $"{FormatShortDate(local)} {Format(local, TimeFormat(withSeconds))}";
    }

    /// <summary>The long date and the time, such as "Sunday, September 27 2026 14:05".</summary>
    public string FormatLongDateTime(DateTimeOffset value, bool withSeconds = false)
    {
        var local = ToLocal(value);
        return $"{Format(local, settings.Formats.LongDateFormat)} {Format(local, TimeFormat(withSeconds))}";
    }

    private string TimeFormat(bool withSeconds) =>
        withSeconds ? settings.Formats.TimeFormat.Replace("mm", "mm:ss", StringComparison.Ordinal) : settings.Formats.TimeFormat;

    private string FormatShortDate(DateTimeOffset local)
    {
        if (!settings.ShowRelativeDates)
        {
            return Format(local, settings.Formats.ShortDateFormat);
        }

        var today = ToLocal(timeProvider.GetUtcNow()).Date;
        return (today - local.Date).Days switch
        {
            0 => "Today",
            1 => "Yesterday",
            > 1 and < 7 => Format(local, "dddd"),
            _ when local.Year == today.Year => Format(local, YearPattern().Replace(settings.Formats.ShortDateFormat, "")),
            _ => Format(local, settings.Formats.ShortDateFormat),
        };
    }

    private DateTimeOffset ToLocal(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, timeProvider.LocalTimeZone);

    private static string Format(DateTimeOffset value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    // The year with the separator that joins it to the day or month, which leads or ends every short date format.
    [GeneratedRegex(@"[ /.,-]*y+[ /.,-]*")]
    private static partial Regex YearPattern();
}
