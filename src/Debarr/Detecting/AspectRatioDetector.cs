using System.ComponentModel;
using System.Globalization;
using FluentResults;

namespace Debarr.Detecting;

/// <summary>
/// Detects one file's aspect ratio with ffprobe and ffmpeg.
/// It takes the container ratio unless that snaps to a ratio marked Check Picture, and measures the picture with cropdetect when it does.
/// </summary>
public sealed class AspectRatioDetector(FfprobeRunner ffprobeRunner, CropDetectRunner cropDetectRunner, FfmpegVersions ffmpegVersions)
    : IAspectRatioDetector
{
    /// <summary>Incremented whenever the detector's output for a file can change. Each detection records it.</summary>
    public const int Version = 1;

    public async Task<DetectorOutcome> DetectAsync(
        string path,
        DetectionSettings detectionSettings,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(detectionSettings.TimeoutSeconds));

        string? ffmpegVersionText = null;
        ContainerMetadata? metadata = null;
        var samples = new List<CropSample>();

        DetectorOutcome Outcome(Result<DetectionResult> result) => new(ffmpegVersionText, metadata, result);

        try
        {
            var ffmpegVersionResult = await ffmpegVersions.GetFfmpegAsync(cancellationToken);
            if (ffmpegVersionResult.IsFailed)
            {
                return Outcome(Result.Fail(ffmpegVersionResult.Errors));
            }

            ffmpegVersionText = ffmpegVersionResult.Value;

            var metadataResult = await ffprobeRunner.ProbeAsync(path, timeout.Token);
            if (metadataResult.IsFailed)
            {
                return Outcome(Result.Fail(metadataResult.Errors));
            }

            (metadata, var duration) = metadataResult.Value;

            if (!detectionSettings.StandardRatios.Snap(metadata.ContainerAspectRatio).ChecksPicture)
            {
                return Outcome(DetectionResult.FromFile(metadata.ContainerAspectRatio));
            }

            return Outcome(await DetectWithCropDetectAsync(path, metadata, duration, detectionSettings.PictureMeasurement, samples, timeout.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Outcome(Result.Fail(string.Create(
                CultureInfo.InvariantCulture,
                $"timed out after {detectionSettings.TimeoutSeconds}s ({samples.Count}/{detectionSettings.PictureMeasurement.SampleCount} samples)")));
        }
        catch (Win32Exception exception)
        {
            return Outcome(Result.Fail($"could not run ffmpeg or ffprobe: {exception.Message}"));
        }
    }

    /// <summary>Measures the picture with cropdetect, adding each sample to <paramref name="samples"/> as it completes.</summary>
    private async Task<Result<DetectionResult>> DetectWithCropDetectAsync(
        string path,
        ContainerMetadata metadata,
        TimeSpan duration,
        PictureMeasurement pictureMeasurement,
        List<CropSample> samples,
        CancellationToken cancellationToken)
    {
        if (duration <= TimeSpan.Zero)
        {
            return Result.Fail("ffprobe reported no duration to place samples in");
        }

        var limit = pictureMeasurement.GetBlackLevel(metadata);

        foreach (var position in pictureMeasurement.GetSamplePositions(duration))
        {
            var sampleResult = await cropDetectRunner.SampleAsync(path, position, limit, cancellationToken);
            if (sampleResult.IsFailed)
            {
                return Result.Fail(sampleResult.Errors);
            }

            samples.Add(sampleResult.Value);
        }

        if (samples.AggregateAspectRatio() is not { } aggregate)
        {
            return Result.Fail($"no crop detected in {samples.Count} samples");
        }

        return new DetectionResult(
            AspectRatioSource.Detected,
            new AspectRatio(Math.Round(aggregate.AspectRatio, 4)),
            aggregate.Confidence,
            samples);
    }
}
