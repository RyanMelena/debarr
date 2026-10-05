using Debarr.Detecting;

namespace Debarr.Components.Pages.Settings;

/// <summary>The detection settings as Settings &gt; Detection edits them.</summary>
public sealed record DetectionSettingsForm
{
    public int SimultaneousDetections { get; set; }

    public int SampleCount { get; set; }

    public int SkipStartAndEndPercent { get; set; }

    public int TimeoutSeconds { get; set; }

    public int BlackLevelSdr { get; set; }

    /// <summary>Null uses <see cref="BlackLevelSdr"/>.</summary>
    public int? BlackLevelHdr { get; set; }

    /// <summary>The standard ratios, smallest first.</summary>
    public List<StandardRatioForm> StandardRatios { get; set; } = [];

    public double MatchTolerance { get; set; }

    public static DetectionSettingsForm FromSettings(DetectionSettings settings) => new()
    {
        SimultaneousDetections = settings.SimultaneousDetections,
        SampleCount = settings.PictureMeasurement.SampleCount,
        SkipStartAndEndPercent = settings.PictureMeasurement.SkipStartAndEndPercent,
        TimeoutSeconds = settings.TimeoutSeconds,
        BlackLevelSdr = settings.PictureMeasurement.BlackLevelSdr,
        BlackLevelHdr = settings.PictureMeasurement.BlackLevelHdr,
        StandardRatios = [.. settings.StandardRatios.Ratios.Select(StandardRatioForm.FromStandardRatio)],
        MatchTolerance = settings.StandardRatios.MatchTolerance,
    };

    /// <summary>A copy whose standard ratios edit apart from this form's.</summary>
    public DetectionSettingsForm Copy() => this with { StandardRatios = [.. StandardRatios.Select(row => row with { })] };

    /// <summary>Whether a setting or a standard ratio differs from <paramref name="saved"/>.</summary>
    public bool HasChangesFrom(DetectionSettingsForm saved) =>
        this with { StandardRatios = saved.StandardRatios } != saved || !StandardRatios.SequenceEqual(saved.StandardRatios);

    public ChangeDetectionSettings ToChangeDetectionSettings() => new(
        SimultaneousDetections,
        SampleCount,
        SkipStartAndEndPercent,
        TimeoutSeconds,
        BlackLevelSdr,
        BlackLevelHdr,
        [.. StandardRatios.Select(row => row.ToStandardRatio())],
        MatchTolerance);
}
