using System.Globalization;
using System.Text.Json;
using CliWrap;
using CliWrap.Buffered;
using Debarr.Hosting;
using FluentResults;
using Microsoft.Extensions.Options;

namespace Debarr.Detecting;

/// <summary>Reads a file's container metadata and duration with ffprobe, from the first video stream.</summary>
public sealed class FfprobeRunner(IOptions<FfmpegOptions> options)
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task<Result<(ContainerMetadata ContainerMetadata, TimeSpan Duration)>> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var result = await Cli.Wrap(options.Value.FfprobePath)
            .WithArguments(
            [
                "-v", "error",
                "-print_format", "json",
                "-show_format", "-show_streams",
                "-select_streams", "v:0",
                path,
            ])
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        if (result.ExitCode != 0)
        {
            return Result.Fail($"ffprobe exited {result.ExitCode}: {result.StandardError.Trim()}");
        }

        return Parse(result.StandardOutput);
    }

    public static Result<(ContainerMetadata ContainerMetadata, TimeSpan Duration)> Parse(string json)
    {
        ProbeOutput? output;
        try
        {
            output = JsonSerializer.Deserialize<ProbeOutput>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return Result.Fail("ffprobe output is not valid JSON.");
        }

        if (output?.Streams is not [var stream, ..])
        {
            return Result.Fail("ffprobe found no video stream.");
        }

        if (stream is not { Width: > 0 and var width, Height: > 0 and var height })
        {
            return Result.Fail("ffprobe reported no width and height for the video stream.");
        }

        var duration = ParseSeconds(stream.Duration) ?? ParseSeconds(output.Format?.Duration) ?? TimeSpan.Zero;

        var containerAspectRatio = new AspectRatio(width * ParsePixelAspectRatio(stream.SampleAspectRatio) / height);
        return (new ContainerMetadata(containerAspectRatio, width, height, stream.CodecName ?? "", stream.ColorTransfer), duration);
    }

    // ffprobe writes "N/A" when a duration is unknown.
    private static TimeSpan? ParseSeconds(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : null;

    // ffprobe writes "N/A" or "0:1" when the ratio is unknown, which counts as square pixels.
    private static double ParsePixelAspectRatio(string? sampleAspectRatio) =>
        sampleAspectRatio?.Split(':') is [var numerator, var denominator]
        && int.TryParse(numerator, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeratorValue)
        && int.TryParse(denominator, NumberStyles.Integer, CultureInfo.InvariantCulture, out var denominatorValue)
        && numeratorValue > 0
        && denominatorValue > 0
            ? (double)numeratorValue / denominatorValue
            : 1;

    private sealed record ProbeOutput(IReadOnlyList<ProbeStream>? Streams, ProbeFormat? Format);

    private sealed record ProbeStream(
        string? CodecName,
        int? Width,
        int? Height,
        string? SampleAspectRatio,
        string? ColorTransfer,
        string? Duration);

    private sealed record ProbeFormat(string? Duration);
}
