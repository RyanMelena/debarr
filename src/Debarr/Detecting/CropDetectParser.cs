using System.Globalization;
using System.Text.RegularExpressions;

namespace Debarr.Detecting;

/// <summary>Reads the picture area from cropdetect's stderr.</summary>
public static partial class CropDetectParser
{
    [GeneratedRegex(@"crop=(-?\d+):(-?\d+):-?\d+:-?\d+", RegexOptions.CultureInvariant)]
    private static partial Regex CropPattern();

    /// <summary>
    /// The last box with a positive width and height. With reset=0 it covers every frame in the window.
    /// Null when cropdetect found no picture, which it reports as a negative width and height.
    /// </summary>
    public static CropBox? ParseLast(IEnumerable<string> lines)
    {
        CropBox? last = null;

        foreach (var line in lines)
        {
            foreach (Match match in CropPattern().Matches(line))
            {
                var box = new CropBox(ParseInt32(match.Groups[1]), ParseInt32(match.Groups[2]));

                if (box is { Width: > 0, Height: > 0 })
                {
                    last = box;
                }
            }
        }

        return last;
    }

    private static int ParseInt32(Group group) => int.Parse(group.ValueSpan, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
}
