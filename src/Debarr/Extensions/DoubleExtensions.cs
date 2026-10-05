using System.Globalization;

namespace Debarr.Extensions;

public static class DoubleExtensions
{
    /// <summary>A snapped, sent or override ratio, with two decimals or as many more as it holds, up to four, such as "2.40" or "2.355".</summary>
    public static string ToAspectRatioText(this double aspectRatio) =>
        aspectRatio.ToString("0.00##", CultureInfo.InvariantCulture);

    /// <summary>A measured ratio before snapping, with three decimals, such as "2.387".</summary>
    public static string ToRawAspectRatioText(this double aspectRatio) =>
        aspectRatio.ToString("0.000", CultureInfo.InvariantCulture);

    /// <summary>A fraction from 0 to 1 as a whole percentage, such as "92%".</summary>
    public static string ToPercentText(this double fraction) =>
        fraction.ToString("0%", CultureInfo.InvariantCulture);
}
