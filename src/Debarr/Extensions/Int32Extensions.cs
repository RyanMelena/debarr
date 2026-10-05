using System.Globalization;

namespace Debarr.Extensions;

public static class Int32Extensions
{
    /// <summary>A count with thousands separators and the noun that agrees with it, such as "1 video file" or "1,204 video files".</summary>
    public static string ToCountText(this int count, string singular, string plural) =>
        string.Create(CultureInfo.InvariantCulture, $"{count:N0} {(count == 1 ? singular : plural)}");

    /// <summary>A count with thousands separators, such as "1,204".</summary>
    public static string ToCountText(this int count) =>
        count.ToString("N0", CultureInfo.InvariantCulture);
}
