using System.Text.Json.Serialization;
using Fisher;

namespace Debarr.Detecting;

/// <summary>How detection runs: the simultaneous detections, the picture measurement, the timeout, and the standard ratios with their match tolerance.</summary>
/// <param name="SimultaneousDetections">How many queued detections run at once, sized to the disks the library sits on.</param>
/// <param name="TimeoutSeconds">The limit for one detection.</param>
public sealed record DetectionSettings(int SimultaneousDetections, PictureMeasurement PictureMeasurement, int TimeoutSeconds, StandardRatios StandardRatios)
{
    /// <summary>The one stream the detection settings' events are appended to.</summary>
    public static readonly Guid StreamId = new("fcd45448-af9d-4cd7-a5c3-adaf8c956c69");

    /// <summary>The settings before the first save, which a missing stream folds to.</summary>
    public static DetectionSettings Default { get; } = new(2, new PictureMeasurement(12, 5, 24, null), 900, StandardRatios.Default);

    public static async Task<DetectionSettings> ReadAsync(IQuerySession session, CancellationToken cancellationToken) =>
        await session.Events.FetchLatest<DetectionSettings>(StreamId, cancellationToken) ?? Default;

    /// <summary>The stream's id, which the event store keys the aggregate on.</summary>
    public Guid Id => StreamId;

    public static DetectionSettings Create(DetectionSettingsChanged changed) => Default.Apply(changed);

    public DetectionSettings Apply(DetectionSettingsChanged changed) => this with
    {
        SimultaneousDetections = changed.SimultaneousDetections,
        PictureMeasurement = changed.PictureMeasurement,
        TimeoutSeconds = changed.TimeoutSeconds,
    };

    public DetectionSettings Apply(StandardRatiosChanged changed) => this with { StandardRatios = changed.StandardRatios };
}

/// <summary>Which current results a change of the standard ratios re-checks: every one, those whose container ratio snaps to one of some standard ratios, or none.</summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(All), "all")]
[JsonDerivedType(typeof(Some), "some")]
[JsonDerivedType(typeof(None), "none")]
public abstract record RecheckScope
{
    private RecheckScope()
    {
    }

    /// <summary>The match tolerance changed, or a standard ratio was added or removed.</summary>
    public sealed record All : RecheckScope;

    /// <param name="AspectRatios">The standard ratios, rounded for comparison, whose Check Picture changed.</param>
    public sealed record Some(IReadOnlyList<double> AspectRatios) : RecheckScope;

    /// <summary>The standard ratios and the match tolerance are unchanged.</summary>
    public sealed record None : RecheckScope;

    /// <summary>
    /// Every current result when the match tolerance changed or a standard ratio was added or removed,
    /// otherwise those that snap to a standard ratio whose Check Picture changed.
    /// </summary>
    public static RecheckScope ForChange(StandardRatios from, StandardRatios to)
    {
        var checksPictureFrom = from.Ratios.ToDictionary(standardRatio => StandardRatios.RoundForComparison(standardRatio.AspectRatio), standardRatio => standardRatio.ChecksPicture);
        var checksPictureTo = to.Ratios.ToDictionary(standardRatio => StandardRatios.RoundForComparison(standardRatio.AspectRatio), standardRatio => standardRatio.ChecksPicture);

        if (from.MatchTolerance != to.MatchTolerance || !checksPictureFrom.Keys.ToHashSet().SetEquals(checksPictureTo.Keys))
        {
            return new All();
        }

        List<double> changed = [.. checksPictureTo.Where(pair => checksPictureFrom[pair.Key] != pair.Value).Select(pair => pair.Key)];
        return changed.Count > 0 ? new Some(changed) : new None();
    }

    public bool Includes(StandardRatio? match) => this switch
    {
        All => true,
        Some some => match is not null && some.AspectRatios.Contains(StandardRatios.RoundForComparison(match.AspectRatio)),
        _ => false,
    };
}
