namespace Debarr.Detecting;

/// <summary>A raw aspect ratio matched to the standard ratios: the matched standard ratio, or the raw ratio rounded to two decimals when none is near.</summary>
/// <param name="Match">The nearest standard ratio within the match tolerance; null when none is.</param>
public sealed record SnappedAspectRatio(double Value, StandardRatio? Match)
{
    public bool ChecksPicture => Match?.ChecksPicture == true;
}
