using FluentResults;

namespace Debarr.Detecting;

/// <summary>How cropdetect measures the picture: the sample count, the share skipped at each end, and the black levels.</summary>
/// <param name="SkipStartAndEndPercent">The share of the runtime skipped at each end.</param>
/// <param name="BlackLevelSdr">The cropdetect black threshold on an 8-bit 0-255 scale.</param>
/// <param name="BlackLevelHdr">The threshold for PQ and HLG files; null uses <paramref name="BlackLevelSdr"/>.</param>
public sealed record PictureMeasurement(int SampleCount, int SkipStartAndEndPercent, int BlackLevelSdr, int? BlackLevelHdr)
{
    /// <summary>The picture measurement, or a field error for each value outside its bounds.</summary>
    public static Result<PictureMeasurement> Create(int sampleCount, int skipStartAndEndPercent, int blackLevelSdr, int? blackLevelHdr)
    {
        var errors = new List<IError>();
        if (sampleCount < 1)
        {
            errors.Add(new FieldError(nameof(SampleCount), "Enter 1 or more."));
        }

        if (skipStartAndEndPercent is < 0 or > 45)
        {
            errors.Add(new FieldError(nameof(SkipStartAndEndPercent), "Enter 0 to 45."));
        }

        if (blackLevelSdr is < 0 or > 254)
        {
            errors.Add(new FieldError(nameof(BlackLevelSdr), "Enter 0 to 254."));
        }

        if (blackLevelHdr is < 0 or > 254)
        {
            errors.Add(new FieldError(nameof(BlackLevelHdr), "Enter 0 to 254."));
        }

        return errors.Count > 0
            ? Result.Fail(errors)
            : new PictureMeasurement(sampleCount, skipStartAndEndPercent, blackLevelSdr, blackLevelHdr);
    }

    /// <summary>The black level for the file: the HDR level for PQ and HLG when one is set, and the SDR level otherwise.</summary>
    public int GetBlackLevel(ContainerMetadata containerMetadata) =>
        containerMetadata.IsHdr ? BlackLevelHdr ?? BlackLevelSdr : BlackLevelSdr;

    /// <summary>
    /// Evenly spaced sample starts across the runtime left after skipping <see cref="SkipStartAndEndPercent"/> percent at each end,
    /// each centred in its share of that span.
    /// </summary>
    public IReadOnlyList<TimeSpan> GetSamplePositions(TimeSpan duration)
    {
        var start = duration * (SkipStartAndEndPercent / 100.0);
        var span = duration * (1 - 2 * SkipStartAndEndPercent / 100.0);

        return [.. Enumerable.Range(0, Math.Max(SampleCount, 0)).Select(index => start + span * ((index + 0.5) / SampleCount))];
    }
}
