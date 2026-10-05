using System.Globalization;
using CliWrap;
using Debarr.Hosting;
using FluentResults;
using Microsoft.Extensions.Options;

namespace Debarr.Detecting;

/// <summary>Runs ffmpeg's cropdetect over one second of a file, and returns the picture area it found.</summary>
public sealed class CropDetectRunner(IOptions<FfmpegOptions> options)
{
    private const int ErrorTailLineCount = 5;

    // cropdetect reads a limit below 1 as a fraction of the file's own bit depth and anything else as a
    // raw code value, so the fraction stays below 1 to keep 10-bit and 12-bit files on the same scale.
    private const double LimitScale = 255.0;
    private const double MaxLimitFraction = 254.0 / LimitScale;

    /// <summary>Samples one second from <paramref name="position"/>, with <paramref name="limit"/> on the 8-bit black scale.</summary>
    public async Task<Result<CropSample>> SampleAsync(string path, TimeSpan position, int limit, CancellationToken cancellationToken)
    {
        var lines = new List<string>();

        var result = await Cli.Wrap(options.Value.FfmpegPath)
            .WithArguments(
            [
                "-hide_banner", "-nostats", "-v", "info",
                "-ss", position.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                "-t", "1",
                "-i", path,
                // The first video stream, since ffmpeg's default can be an MKV's attached cover art.
                "-map", "0:v:0", "-an", "-sn", "-dn",
                // scale and setsar make pixels square, and round=2 keeps a borderline 1.85 crop at 1.85.
                "-vf", string.Create(
                    CultureInfo.InvariantCulture,
                    $"scale=iw*sar:ih,setsar=1,cropdetect=limit={FormatLimit(limit)}:round=2:reset=0"),
                "-f", "null", "-",
            ])
            .WithValidation(CommandResultValidation.None)
            .WithStandardErrorPipe(PipeTarget.ToDelegate(lines.Add))
            .ExecuteAsync(cancellationToken);

        if (result.ExitCode != 0)
        {
            var tail = string.Join(" | ", lines.TakeLast(ErrorTailLineCount).Select(line => line.Trim()));

            return Result.Fail(string.Create(
                CultureInfo.InvariantCulture,
                $"ffmpeg exited {result.ExitCode} sampling at {position.TotalSeconds:0.###}s: {tail}"));
        }

        return new CropSample(position, CropDetectParser.ParseLast(lines));
    }

    public static string FormatLimit(int limit) =>
        Math.Min(Math.Max(limit, 0) / LimitScale, MaxLimitFraction).ToString("0.######", CultureInfo.InvariantCulture);
}
