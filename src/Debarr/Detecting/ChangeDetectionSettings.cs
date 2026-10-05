using Debarr.Scanning;
using Fisher;
using FluentResults;
using Wolverine.Fisher;
using Wolverine.Persistence.EventSourcing;

namespace Debarr.Detecting;

/// <summary>
/// Saves the settings Settings &gt; Detection edits.
/// A change to the standard ratios or the match tolerance clears or converts each current result it affects, in the same transaction.
/// </summary>
/// <param name="BlackLevelHdr">Null uses <paramref name="BlackLevelSdr"/>.</param>
public sealed record ChangeDetectionSettings(
    int SimultaneousDetections,
    int SampleCount,
    int SkipStartAndEndPercent,
    int TimeoutSeconds,
    int BlackLevelSdr,
    int? BlackLevelHdr,
    IReadOnlyList<StandardRatio> StandardRatios,
    double MatchTolerance)
{
    /// <summary>The detection settings' stream, which Wolverine loads the aggregate from.</summary>
    public Guid DetectionSettingsId => DetectionSettings.StreamId;
}

public static class ChangeDetectionSettingsHandler
{
    /// <summary>
    /// Refuses a value outside its bounds, an empty list of standard ratios, a standard ratio listed twice, and a match tolerance under its floor,
    /// each beneath its field.
    /// Parses a valid command, with the recheck scope of its standard ratios against the stored ones.
    /// </summary>
    public static Result<ParsedDetectionSettings> Validate(ChangeDetectionSettings command, DetectionSettings? settings)
    {
        var errors = new List<IError>();
        if (command.SimultaneousDetections < 1)
        {
            errors.Add(new FieldError(nameof(ChangeDetectionSettings.SimultaneousDetections), "Enter 1 or more."));
        }

        if (command.TimeoutSeconds < 1)
        {
            errors.Add(new FieldError(nameof(ChangeDetectionSettings.TimeoutSeconds), "Enter 1 or more."));
        }

        var pictureMeasurement = PictureMeasurement.Create(command.SampleCount, command.SkipStartAndEndPercent, command.BlackLevelSdr, command.BlackLevelHdr);
        var standardRatios = StandardRatios.Create(command.StandardRatios, command.MatchTolerance);
        var merged = Result.Merge(errors.Count > 0 ? Result.Fail(errors) : Result.Ok(), pictureMeasurement.ToResult(), standardRatios.ToResult());
        if (merged.IsFailed)
        {
            return Result.Fail<ParsedDetectionSettings>(merged.Errors);
        }

        var recheckScope = RecheckScope.ForChange((settings ?? DetectionSettings.Default).StandardRatios, standardRatios.Value);
        return Result.Ok(new ParsedDetectionSettings(pictureMeasurement.Value, standardRatios.Value, recheckScope));
    }

    /// <summary>
    /// For a valid command that changes the standard ratios, starts a library write pause: it stops the running scan and pauses scans, since a scan's commit to a video file the re-check reaches would fail its attempt,
    /// and pauses detections, so a running detection, which snaps with the old ones, is cancelled and runs again under the new ones.
    /// Wolverine runs it inside the command's log scope once the settings are loaded.
    /// </summary>
    public static async Task<LibraryWritePause?> BeforeAsync(
        ChangeDetectionSettings command,
        DetectionSettings? settings,
        LibraryScanner scanner,
        DetectionOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        var validated = Validate(command, settings);
        return validated.IsSuccess && validated.Value.RecheckScope is not RecheckScope.None
            ? await LibraryWritePause.StartAsync(scanner, orchestrator, cancellationToken)
            : null;
    }

    /// <summary>
    /// Ends the pause <see cref="BeforeAsync"/> returned, from the generated handler's <c>finally</c> block when the command finishes.
    /// The generated handler also holds the pause in a <c>using</c>, which disposes it again afterwards, and the pause ends once.
    /// </summary>
    public static void Finally(LibraryWritePause? pause) => pause?.Dispose();

    /// <summary>The Media rows a standard ratios change re-checks from; none when the command keeps the standard ratios and the match tolerance.</summary>
    public static async Task<IReadOnlyList<MediaRow>> LoadAsync(
        ChangeDetectionSettings command,
        ParsedDetectionSettings parsed,
        IQuerySession session,
        CancellationToken cancellationToken) =>
        parsed.RecheckScope is not RecheckScope.None ? await session.ReadWithContainerMetadataAsync(cancellationToken) : [];

    /// <summary>
    /// The settings, and when the standard ratios or the match tolerance changed, the new ones,
    /// with an append to each video file whose current result the change clears or converts.
    /// </summary>
    public static (Events, IEnumerable<IFisherOp>) Handle(
        ChangeDetectionSettings command,
        [WriteModel(Required = false)] DetectionSettings? settings,
        ParsedDetectionSettings parsed,
        IReadOnlyList<MediaRow> currentResults,
        TimeProvider timeProvider)
    {
        var changed = new DetectionSettingsChanged(command.SimultaneousDetections, parsed.PictureMeasurement, command.TimeoutSeconds);
        if (parsed.RecheckScope is RecheckScope.None)
        {
            return ([changed], []);
        }

        return ([changed, new StandardRatiosChanged(parsed.StandardRatios, parsed.RecheckScope)], [.. Recheck(currentResults, parsed.StandardRatios, parsed.RecheckScope, timeProvider.GetUtcNow())]);
    }

    /// <summary>Clears or converts each current result as <see cref="Recheck(AspectRatioSource, AspectRatio, StandardRatios, RecheckScope)"/> decides, which also clears its video file's last failure.</summary>
    private static IEnumerable<IFisherOp> Recheck(IEnumerable<MediaRow> currentResults, StandardRatios standardRatios, RecheckScope scope, DateTimeOffset now)
    {
        foreach (var row in currentResults)
        {
            if (row.CurrentResult is not { ContainerMetadata: { } containerMetadata } result)
            {
                continue;
            }

            switch (Recheck(result.Source, containerMetadata.ContainerAspectRatio, standardRatios, scope))
            {
                case CurrentResultRecheck.Clear:
                    yield return FisherOps.Append(row.Id, row.Version, new DetectionResultCleared(now));
                    break;
                case CurrentResultRecheck.Convert:
                    var converted = ConvertToFromFile(Guid.CreateVersion7(), result.DetectorVersion, result.FfmpegVersion, containerMetadata, now);
                    yield return FisherOps.Append(row.Id, row.Version, new DetectionResultConverted(row.FileHash, converted));
                    break;
            }
        }
    }

    /// <summary>
    /// What a standard ratios change does to a current result whose container ratio snaps inside its scope:
    /// a result from the file whose container ratio now checks the picture is cleared,
    /// and a detected result whose container ratio no longer does is converted to a result from the file.
    /// </summary>
    private static CurrentResultRecheck Recheck(AspectRatioSource source, AspectRatio containerAspectRatio, StandardRatios standardRatios, RecheckScope scope)
    {
        var snapped = standardRatios.Snap(containerAspectRatio);
        return (scope.Includes(snapped.Match), source, snapped.ChecksPicture) switch
        {
            (true, AspectRatioSource.FromFile, true) => CurrentResultRecheck.Clear,
            (true, AspectRatioSource.Detected, false) => CurrentResultRecheck.Convert,
            _ => CurrentResultRecheck.Keep,
        };
    }

    /// <summary>
    /// The result from the file that replaces a detected result a standard ratios change converts:
    /// started by the change, from the replaced detection's container metadata, and keeping its versions.
    /// </summary>
    private static Detection ConvertToFromFile(Guid id, int detectorVersion, string? ffmpegVersion, ContainerMetadata containerMetadata, DateTimeOffset now) => new(
        id,
        DetectionOrigin.StandardRatiosChange,
        Path: null,
        now,
        Duration: TimeSpan.Zero,
        detectorVersion,
        ffmpegVersion,
        containerMetadata,
        new DetectionOutcome.Succeeded(DetectionResult.FromFile(containerMetadata.ContainerAspectRatio)));

    private enum CurrentResultRecheck
    {
        Keep,
        Clear,
        Convert,
    }
}
