using System.Globalization;

namespace Debarr.Extensions;

public static class TimeSpanExtensions
{
    /// <summary>The duration in its two largest units, such as "850 ms", "3.2 s", "4 min 5 s" or "2 h 3 min".</summary>
    public static string ToDisplayText(this TimeSpan duration) => duration switch
    {
        { TotalSeconds: < 1 } => $"{duration.Milliseconds} ms",
        { TotalMinutes: < 1 } => string.Create(CultureInfo.InvariantCulture, $"{duration.TotalSeconds:0.#} s"),
        { TotalHours: < 1 } => $"{duration.Minutes} min {duration.Seconds} s",
        _ => $"{(int)duration.TotalHours} h {duration.Minutes} min",
    };
}
