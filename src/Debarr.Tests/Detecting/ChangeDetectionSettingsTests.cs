using Debarr.Detecting;
using Debarr.Scanning;
using Microsoft.Extensions.Time.Testing;
using Wolverine.Fisher;

namespace Debarr.Tests.Detecting;

public sealed class ChangeDetectionSettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 20, 15, 0, TimeSpan.Zero);

    private static readonly ChangeDetectionSettings Unchanged = Change(DetectionSettings.Default);

    public static TheoryData<ChangeDetectionSettings, string, string> OutOfBounds => new()
    {
        { Unchanged with { SimultaneousDetections = 0 }, nameof(ChangeDetectionSettings.SimultaneousDetections), "Enter 1 or more." },
        { Unchanged with { TimeoutSeconds = 0 }, nameof(ChangeDetectionSettings.TimeoutSeconds), "Enter 1 or more." },
        { Unchanged with { SampleCount = 0 }, nameof(ChangeDetectionSettings.SampleCount), "Enter 1 or more." },
        { Unchanged with { SkipStartAndEndPercent = -1 }, nameof(ChangeDetectionSettings.SkipStartAndEndPercent), "Enter 0 to 45." },
        { Unchanged with { SkipStartAndEndPercent = 46 }, nameof(ChangeDetectionSettings.SkipStartAndEndPercent), "Enter 0 to 45." },
        { Unchanged with { BlackLevelSdr = -1 }, nameof(ChangeDetectionSettings.BlackLevelSdr), "Enter 0 to 254." },
        { Unchanged with { BlackLevelSdr = 255 }, nameof(ChangeDetectionSettings.BlackLevelSdr), "Enter 0 to 254." },
        { Unchanged with { BlackLevelHdr = -1 }, nameof(ChangeDetectionSettings.BlackLevelHdr), "Enter 0 to 254." },
        { Unchanged with { BlackLevelHdr = 255 }, nameof(ChangeDetectionSettings.BlackLevelHdr), "Enter 0 to 254." },
        { Unchanged with { StandardRatios = [] }, nameof(ChangeDetectionSettings.StandardRatios), "Add at least one standard ratio." },
        {
            Unchanged with { StandardRatios = [.. Unchanged.StandardRatios, new StandardRatio(1.851, true)] },
            nameof(ChangeDetectionSettings.StandardRatios),
            "1.85 is listed twice."
        },
        { Unchanged with { MatchTolerance = 0.009 }, nameof(ChangeDetectionSettings.MatchTolerance), "Enter 0.01 or more." },
    };

    [Theory]
    [MemberData(nameof(OutOfBounds))]
    public void A_value_outside_its_bounds_is_refused_beneath_its_field(ChangeDetectionSettings command, string field, string message)
    {
        var error = Assert.IsType<FieldError>(Assert.Single(ChangeDetectionSettingsHandler.Validate(command, DetectionSettings.Default).Errors));

        Assert.Equal((field, message), (error.Field, error.Message));
    }

    [Fact]
    public void Every_value_outside_its_bounds_is_refused_at_once()
    {
        var command = Unchanged with { SimultaneousDetections = 0, TimeoutSeconds = 0, SampleCount = 0, StandardRatios = [], MatchTolerance = 0 };

        var fields = ChangeDetectionSettingsHandler.Validate(command, DetectionSettings.Default).Errors.Cast<FieldError>().Select(error => error.Field);

        Assert.Equal(
            [
                nameof(ChangeDetectionSettings.SimultaneousDetections),
                nameof(ChangeDetectionSettings.TimeoutSeconds),
                nameof(ChangeDetectionSettings.SampleCount),
                nameof(ChangeDetectionSettings.StandardRatios),
                nameof(ChangeDetectionSettings.MatchTolerance),
            ],
            fields);
    }

    [Fact]
    public void Values_at_their_bounds_are_accepted()
    {
        var command = Unchanged with
        {
            SimultaneousDetections = 1,
            TimeoutSeconds = 1,
            SampleCount = 1,
            SkipStartAndEndPercent = 45,
            BlackLevelSdr = 254,
            BlackLevelHdr = 0,
            StandardRatios = [new StandardRatio(2.39, false)],
            MatchTolerance = 0.01,
        };

        Assert.True(ChangeDetectionSettingsHandler.Validate(command, DetectionSettings.Default).IsSuccess);
    }

    [Fact]
    public void A_save_with_the_same_standard_ratios_changes_only_the_settings()
    {
        var command = Unchanged with { SimultaneousDetections = 4, SampleCount = 20, SkipStartAndEndPercent = 10, TimeoutSeconds = 60, BlackLevelSdr = 16, BlackLevelHdr = 64 };

        var changed = Assert.IsType<DetectionSettingsChanged>(Assert.Single(SettingsEvents(command, DetectionSettings.Default)));

        Assert.Equal(new DetectionSettingsChanged(4, new PictureMeasurement(20, 10, 16, 64), 60), changed);
        var settings = DetectionSettings.Default.Apply(changed);
        Assert.Equal((4, new PictureMeasurement(20, 10, 16, 64), 60), (settings.SimultaneousDetections, settings.PictureMeasurement, settings.TimeoutSeconds));
        Assert.Same(DetectionSettings.Default.StandardRatios, settings.StandardRatios);
    }

    [Fact]
    public void A_save_before_the_first_one_validates_and_decides_against_the_defaults()
    {
        var command = Unchanged with { SimultaneousDetections = 3 };

        Assert.True(ChangeDetectionSettingsHandler.Validate(command, null).IsSuccess);
        var changed = Assert.IsType<DetectionSettingsChanged>(Assert.Single(SettingsEvents(command, null)));
        Assert.Equal(3, DetectionSettings.Create(changed).SimultaneousDetections);
    }

    [Fact]
    public void A_standard_ratios_change_before_the_first_save_is_decided_against_the_default_ratios()
    {
        var command = Unchanged with { StandardRatios = CheckingPicture(1.85, true) };

        var changed = Assert.IsType<StandardRatiosChanged>(SettingsEvents(command, null)[1]);

        Assert.Equal([1.85], Assert.IsType<RecheckScope.Some>(changed.Recheck).AspectRatios);
    }

    [Fact]
    public void A_changed_match_tolerance_re_checks_every_result()
    {
        var changed = StandardRatiosChanged(Unchanged with { MatchTolerance = 0.05 });

        Assert.Equal((new RecheckScope.All(), 0.05), (changed.Recheck, changed.StandardRatios.MatchTolerance));
        Assert.Equal(0.05, DetectionSettings.Default.Apply(changed).StandardRatios.MatchTolerance);
    }

    [Fact]
    public void An_added_standard_ratio_re_checks_every_result()
    {
        var changed = StandardRatiosChanged(Unchanged with { StandardRatios = [.. Unchanged.StandardRatios, new StandardRatio(2.76, false)] });

        Assert.IsType<RecheckScope.All>(changed.Recheck);
        Assert.Contains(changed.StandardRatios.Ratios, standardRatio => standardRatio.AspectRatio == 2.76);
    }

    [Fact]
    public void A_removed_standard_ratio_re_checks_every_result()
    {
        var changed = StandardRatiosChanged(Unchanged with { StandardRatios = [.. Unchanged.StandardRatios.Where(standardRatio => standardRatio.AspectRatio != 2.2)] });

        Assert.IsType<RecheckScope.All>(changed.Recheck);
        Assert.DoesNotContain(changed.StandardRatios.Ratios, standardRatio => standardRatio.AspectRatio == 2.2);
    }

    [Fact]
    public void A_changed_check_picture_re_checks_only_the_results_that_snap_to_that_ratio()
    {
        var changed = StandardRatiosChanged(Unchanged with { StandardRatios = CheckingPicture(1.78, false) });

        Assert.Equal([1.78], Assert.IsType<RecheckScope.Some>(changed.Recheck).AspectRatios);
        Assert.False(DetectionSettings.Default.Apply(changed).StandardRatios.Ratios.Single(standardRatio => standardRatio.AspectRatio == 1.78).ChecksPicture);
    }

    [Fact]
    public void Marking_a_ratio_check_picture_clears_a_from_file_result_that_snaps_to_it()
    {
        var flat = Row("flat", AspectRatioSource.FromFile, 1.85);
        var scope = Row("scope", AspectRatioSource.FromFile, 2.39);

        var (_, ops) = Handle(Unchanged with { StandardRatios = CheckingPicture(1.85, true) }, [flat, scope]);

        var append = Assert.IsType<AppendToStream>(Assert.Single(ops));
        Assert.Equal(flat.Id, append.StreamId);
        Assert.Equal([new DetectionResultCleared(Now)], append.Events);
    }

    [Fact]
    public void Unmarking_a_check_picture_ratio_converts_a_detected_result_to_one_from_the_file_with_its_versions()
    {
        var widescreen = Row("widescreen", AspectRatioSource.Detected, 1.78);

        var (_, ops) = Handle(Unchanged with { StandardRatios = CheckingPicture(1.78, false) }, [widescreen]);

        var append = Assert.IsType<AppendToStream>(Assert.Single(ops));
        var converted = Assert.IsType<DetectionResultConverted>(Assert.Single(append.Events));
        Assert.Equal((widescreen.Id, widescreen.FileHash), (append.StreamId, converted.VideoFile));
        Assert.Equal(
            new Detection(Guid.Empty, DetectionOrigin.StandardRatiosChange, null, Now, TimeSpan.Zero, 3, "7.1", Container(1.78), new DetectionOutcome.Succeeded(DetectionResult.FromFile(new AspectRatio(1.78)))),
            converted.Detection with { Id = Guid.Empty });
    }

    [Fact]
    public void A_result_outside_the_scope_of_a_standard_ratios_change_is_kept()
    {
        // 1.78 checks the picture, so a re-check that includes 1.78 clears this result.
        var widescreen = Row("widescreen", AspectRatioSource.FromFile, 1.78);

        var (events, ops) = Handle(Unchanged with { StandardRatios = CheckingPicture(2.39, true) }, [widescreen]);

        Assert.IsType<StandardRatiosChanged>(events[1]);
        Assert.Empty(ops);
    }

    [Fact]
    public void A_save_that_keeps_the_standard_ratios_re_checks_no_result()
    {
        var widescreen = Row("widescreen", AspectRatioSource.FromFile, 1.78);

        var (events, ops) = Handle(Unchanged with { SimultaneousDetections = 3 }, [widescreen]);

        Assert.IsType<DetectionSettingsChanged>(Assert.Single(events));
        Assert.Empty(ops);
        Assert.IsType<RecheckScope.None>(Parse(Unchanged with { SimultaneousDetections = 3 }, DetectionSettings.Default).RecheckScope);
    }

    private static (Events Events, List<IFisherOp> Ops) Handle(ChangeDetectionSettings command, IReadOnlyList<MediaRow> currentResults)
    {
        var (events, ops) = ChangeDetectionSettingsHandler.Handle(command, DetectionSettings.Default, Parse(command, DetectionSettings.Default), currentResults, new FakeTimeProvider(Now));
        return (events, [.. ops]);
    }

    private static Events SettingsEvents(ChangeDetectionSettings command, DetectionSettings? settings) =>
        ChangeDetectionSettingsHandler.Handle(command, settings, Parse(command, settings), [], new FakeTimeProvider(Now)).Item1;

    private static ParsedDetectionSettings Parse(ChangeDetectionSettings command, DetectionSettings? settings) => ChangeDetectionSettingsHandler.Validate(command, settings).Value;

    /// <summary>A Media row whose current result a detector at version 3 with ffmpeg 7.1 recorded from a container of the given ratio.</summary>
    private static MediaRow Row(string name, AspectRatioSource source, double containerAspectRatio)
    {
        var fileHash = TestFileHash.For(name);
        var result = source == AspectRatioSource.FromFile
            ? DetectionResult.FromFile(new AspectRatio(containerAspectRatio))
            : new DetectionResult(source, new AspectRatio(2.4), 1, []);
        var detection = new Detection(Guid.CreateVersion7(), DetectionOrigin.Queue, new LocalPath("/media/film.mkv"), Now.AddDays(-1), TimeSpan.FromSeconds(9), 3, "7.1", Container(containerAspectRatio), new DetectionOutcome.Succeeded(result));
        return new MediaRow { Id = fileHash.StreamId, FileHash = fileHash, CurrentResult = MediaRowResult.From(detection) };
    }

    private static ContainerMetadata Container(double aspectRatio) => new(new AspectRatio(aspectRatio), 1920, 1080, "hevc", null);

    private static StandardRatiosChanged StandardRatiosChanged(ChangeDetectionSettings command)
    {
        var events = SettingsEvents(command, DetectionSettings.Default);

        Assert.Equal(2, events.Count);
        Assert.IsType<DetectionSettingsChanged>(events[0]);
        return Assert.IsType<StandardRatiosChanged>(events[1]);
    }

    private static List<StandardRatio> CheckingPicture(double aspectRatio, bool checksPicture) =>
        [.. Unchanged.StandardRatios.Select(standardRatio => standardRatio.AspectRatio == aspectRatio ? standardRatio with { ChecksPicture = checksPicture } : standardRatio)];

    private static ChangeDetectionSettings Change(DetectionSettings settings) => new(
        settings.SimultaneousDetections,
        settings.PictureMeasurement.SampleCount,
        settings.PictureMeasurement.SkipStartAndEndPercent,
        settings.TimeoutSeconds,
        settings.PictureMeasurement.BlackLevelSdr,
        settings.PictureMeasurement.BlackLevelHdr,
        settings.StandardRatios.Ratios,
        settings.StandardRatios.MatchTolerance);
}
