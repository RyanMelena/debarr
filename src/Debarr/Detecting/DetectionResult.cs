namespace Debarr.Detecting;

/// <summary>What a successful detection found: the raw ratio, its source, the confidence and the crop samples.</summary>
/// <param name="Samples">Every cropdetect sample in position order; empty for a result from the file.</param>
public sealed record DetectionResult(
    AspectRatioSource AspectRatioSource,
    AspectRatio RawAspectRatio,
    double Confidence,
    IReadOnlyList<CropSample> Samples)
{
    /// <summary>The confidence of a result from the file. A detected result's confidence is its largest group's share of the samples.</summary>
    public const double FromFileConfidence = 0.9;

    /// <summary>The result that accepts the container ratio as it is.</summary>
    public static DetectionResult FromFile(AspectRatio containerAspectRatio) =>
        new(AspectRatioSource.FromFile, new AspectRatio(Math.Round(containerAspectRatio.Value, 4)), FromFileConfidence, []);
}
