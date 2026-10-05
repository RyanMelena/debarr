using Debarr.Detecting;
using Debarr.Hosting;
using Microsoft.Extensions.Options;

namespace Debarr.Tests.Detecting;

public class AspectRatioDetectorTests
{
    private static readonly StandardRatios StandardRatiosAround182 =
        StandardRatios.Create([new(1.78, true), new(2.39, false)], 0.04).Value;

    [Fact]
    public async Task A_239_picture_letterboxed_in_1080p_is_detected_as_239()
    {
        var detected = AssertSuccess(AspectRatioSource.Detected, await DetectAsync("letterbox-239.mkv"));

        Assert.InRange(detected.RawAspectRatio.Value, 2.37, 2.41);
        Assert.Equal(2.39, Snap(detected.RawAspectRatio));
        Assert.Equal(1.0, detected.Confidence);
        Assert.Equal(12, detected.Samples.Count);
    }

    [Fact]
    public async Task A_185_picture_letterboxed_in_1080p_is_detected_as_185()
    {
        var detected = AssertSuccess(AspectRatioSource.Detected, await DetectAsync("letterbox-185.mkv"));

        Assert.Equal(1.85, Snap(detected.RawAspectRatio));
    }

    [Fact]
    public async Task A_full_1080p_picture_is_detected_as_178()
    {
        var detected = AssertSuccess(AspectRatioSource.Detected, await DetectAsync("full-178.mkv"));

        Assert.Equal(1.78, Snap(detected.RawAspectRatio));
    }

    [Fact]
    public async Task A_pillarboxed_43_picture_in_anamorphic_480p_is_detected_as_133()
    {
        var outcome = await DetectAsync("anamorphic-133.mkv");
        var detected = AssertSuccess(AspectRatioSource.Detected, outcome);

        Assert.Equal(1.33, Snap(detected.RawAspectRatio));
        Assert.Equal(1.7778, outcome.ContainerMetadata!.ContainerAspectRatio.Value, 4);
    }

    [Fact]
    public async Task A_container_ratio_that_snaps_to_a_ratio_that_is_not_marked_check_picture_is_taken_as_the_picture_ratio()
    {
        var container = AssertSuccess(AspectRatioSource.FromFile, await DetectAsync("cropped-240.mkv"));

        Assert.Equal(2.4, container.RawAspectRatio.Value);
        Assert.Equal(2.39, Snap(container.RawAspectRatio));
        Assert.Equal(0.9, container.Confidence);
        Assert.Empty(container.Samples);
    }

    [Fact]
    public async Task A_container_ratio_one_tolerance_from_a_check_picture_ratio_runs_cropdetect()
    {
        var detected = AssertSuccess(
            AspectRatioSource.Detected,
            await DetectAsync("full-182.mkv", DetectionSettings.Default with { StandardRatios = StandardRatiosAround182 }));

        Assert.Equal(1.82, detected.RawAspectRatio.Value);
        Assert.Equal(12, detected.Samples.Count);
    }

    [Fact]
    public async Task A_container_ratio_just_outside_the_tolerance_of_a_check_picture_ratio_is_taken_as_the_picture_ratio()
    {
        var container = AssertSuccess(
            AspectRatioSource.FromFile,
            await DetectAsync("full-182.mkv", DetectionSettings.Default with { StandardRatios = StandardRatios.Create(StandardRatiosAround182.Ratios, 0.039).Value }));

        Assert.Equal(1.82, container.RawAspectRatio.Value);
        Assert.Empty(container.Samples);
    }

    [Fact]
    public async Task A_white_bar_in_one_sample_window_lowers_confidence_but_keeps_239()
    {
        var detected = AssertSuccess(AspectRatioSource.Detected, await DetectAsync("burned-bar-239.mkv"));

        Assert.Equal(2.39, Snap(detected.RawAspectRatio));
        Assert.Equal(0.917, detected.Confidence);
        Assert.Equal(new CropBox(1920, 902), detected.Samples[0].Box);
    }

    [Fact]
    public async Task A_239_picture_letterboxed_in_10_bit_pq_is_detected_as_239()
    {
        var outcome = await DetectAsync("letterbox-239-hdr10.mkv");
        var detected = AssertSuccess(AspectRatioSource.Detected, outcome);

        Assert.Equal(2.39, Snap(detected.RawAspectRatio));
        Assert.Equal("smpte2084", outcome.ContainerMetadata!.ColorTransfer);
    }

    [Fact]
    public async Task A_pq_file_samples_with_the_hdr_limit_when_one_is_set()
    {
        // An HDR black level of 0 leaves the bars above the black threshold, so only the full frame is found.
        var detected = AssertSuccess(
            AspectRatioSource.Detected,
            await DetectAsync("letterbox-239-hdr10.mkv", DetectionSettings.Default with { PictureMeasurement = DetectionSettings.Default.PictureMeasurement with { BlackLevelHdr = 0 } }));

        Assert.Equal(1.78, Snap(detected.RawAspectRatio));
    }

    [Fact]
    public async Task A_missing_file_fails_with_the_ffprobe_error_and_the_ffmpeg_version()
    {
        var outcome = await DetectAsync("missing.mkv", requireFixture: false);

        Assert.True(outcome.Result.IsFailed);
        Assert.StartsWith("ffprobe exited 1:", Assert.Single(outcome.Result.Errors).Message);
        Assert.NotNull(outcome.FfmpegVersion);
        Assert.Null(outcome.ContainerMetadata);
    }

    [Fact]
    public async Task A_missing_ffmpeg_executable_fails_with_the_start_error()
    {
        var ffmpegOptions = Options.Create(new FfmpegOptions { FfmpegPath = Path.Combine(AppContext.BaseDirectory, "missing-ffmpeg") });
        var detector = new AspectRatioDetector(
            new FfprobeRunner(ffmpegOptions),
            new CropDetectRunner(ffmpegOptions),
            new FfmpegVersions(ffmpegOptions));

        var outcome = await detector.DetectAsync(
            "missing.mkv",
            DetectionSettings.Default,
            TestContext.Current.CancellationToken);

        Assert.True(outcome.Result.IsFailed);
        Assert.StartsWith("could not run ffmpeg:", Assert.Single(outcome.Result.Errors).Message);
        Assert.Equal((null, null), (outcome.FfmpegVersion, outcome.ContainerMetadata));
    }

    private static DetectionResult AssertSuccess(AspectRatioSource aspectRatioSource, DetectorOutcome outcome)
    {
        var result = outcome.Result;
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Equal(aspectRatioSource, result.Value.AspectRatioSource);

        return result.Value;
    }

    private static double Snap(AspectRatio rawAspectRatio) => StandardRatios.Default.Snap(rawAspectRatio).Value;

    private static async Task<DetectorOutcome> DetectAsync(
        string fixture,
        DetectionSettings? detectionSettings = null,
        bool requireFixture = true)
    {
        Assert.SkipUnless(IsOnPath("ffmpeg") && IsOnPath("ffprobe"), "ffmpeg and ffprobe must be on PATH to run detection against fixtures.");

        var fixtures = Path.Combine(FindRepositoryRoot(), ".dev", "fixtures");
        Assert.SkipWhen(
            requireFixture && !File.Exists(Path.Combine(fixtures, fixture)),
            $"{fixture} is missing from {fixtures}. Run tools/make-detection-fixtures.ps1 to create the fixtures.");

        var ffmpegOptions = Options.Create(new FfmpegOptions());
        var detector = new AspectRatioDetector(
            new FfprobeRunner(ffmpegOptions),
            new CropDetectRunner(ffmpegOptions),
            new FfmpegVersions(ffmpegOptions));

        return await detector.DetectAsync(
            Path.Combine(fixtures, fixture),
            detectionSettings ?? DetectionSettings.Default,
            TestContext.Current.CancellationToken);
    }

    private static bool IsOnPath(string tool)
    {
        var names = OperatingSystem.IsWindows() ? new[] { tool + ".exe", tool } : [tool];

        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => names.Any(name => File.Exists(Path.Combine(directory, name))));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No folder above {AppContext.BaseDirectory} holds AGENTS.md.");
    }
}
