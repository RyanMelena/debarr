using System.Globalization;
using System.Text.Json.Serialization;
using FluentResults;

namespace Debarr.Detecting;

/// <summary>The ratios a raw ratio snaps to, in ascending order, and how far a raw ratio may be from one and still snap to it.</summary>
public sealed class StandardRatios
{
    public const double MinimumMatchTolerance = 0.01;

    [JsonConstructor]
    private StandardRatios(IReadOnlyList<StandardRatio> ratios, double matchTolerance)
    {
        Ratios = ratios;
        MatchTolerance = matchTolerance;
    }

    /// <summary>1.33 and 1.78 marked Check Picture, 1.66, 1.85, 2.00, 2.20, 2.35 and 2.39, with a match tolerance of 0.04.</summary>
    public static StandardRatios Default { get; } = new(
        [
            new(1.33, true),
            new(1.66, false),
            new(1.78, true),
            new(1.85, false),
            new(2.00, false),
            new(2.20, false),
            new(2.35, false),
            new(2.39, false),
        ],
        0.04);

    public IReadOnlyList<StandardRatio> Ratios { get; }

    public double MatchTolerance { get; }

    /// <summary>
    /// The standard ratios, or a field error for an empty list, a ratio of 0 or less, a ratio listed twice when rounded to two decimals,
    /// or a match tolerance under <see cref="MinimumMatchTolerance"/>.
    /// </summary>
    public static Result<StandardRatios> Create(IEnumerable<StandardRatio> ratios, double matchTolerance)
    {
        var list = ratios.OrderBy(ratio => ratio.AspectRatio).ToList();
        var errors = new List<IError>();

        if (list.Count == 0)
        {
            errors.Add(new FieldError(nameof(StandardRatios), "Add at least one standard ratio."));
        }
        else if (list.Any(ratio => !AspectRatio.IsValid(ratio.AspectRatio)))
        {
            errors.Add(new FieldError(nameof(StandardRatios), "Every ratio must be greater than 0."));
        }
        else if (list.GroupBy(ratio => RoundForComparison(ratio.AspectRatio)).FirstOrDefault(group => group.Count() > 1) is { } duplicate)
        {
            errors.Add(new FieldError(nameof(StandardRatios), string.Create(CultureInfo.InvariantCulture, $"{duplicate.Key:0.00} is listed twice.")));
        }

        if (!(matchTolerance >= MinimumMatchTolerance))
        {
            errors.Add(new FieldError(nameof(MatchTolerance), "Enter 0.01 or more."));
        }

        return errors.Count > 0 ? Result.Fail(errors) : new StandardRatios(list, matchTolerance);
    }

    /// <summary>A ratio rounded to two decimals, as two standard ratios are told apart.</summary>
    public static double RoundForComparison(double aspectRatio) => Math.Round(aspectRatio, 2, MidpointRounding.AwayFromZero);

    /// <summary>The nearest standard ratio within the match tolerance, the smaller one on a tie.</summary>
    public SnappedAspectRatio Snap(AspectRatio raw)
    {
        // Rounding the distance absorbs floating-point error, so a ratio one tolerance away matches and equal distances tie.
        var match = Ratios
            .Select(standardRatio => (standardRatio, distance: Math.Round(Math.Abs(standardRatio.AspectRatio - raw.Value), 9)))
            .Where(candidate => candidate.distance <= MatchTolerance)
            .OrderBy(candidate => candidate.distance)
            .ThenBy(candidate => candidate.standardRatio.AspectRatio)
            .Select(candidate => candidate.standardRatio)
            .FirstOrDefault();

        return new SnappedAspectRatio(match?.AspectRatio ?? Math.Round(raw.Value, 2, MidpointRounding.AwayFromZero), match);
    }
}
