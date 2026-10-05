using System.Globalization;
using System.Text.RegularExpressions;

namespace Debarr.Detecting;

/// <summary>What a failed detection's error means to the operator, worked out from the stored error.</summary>
/// <param name="Summary">What went wrong, as a sentence.</param>
/// <param name="Advice">What to do next, as a sentence.</param>
/// <param name="Href">The page where the advice is carried out, relative to the base URL; null when it is the video file's own page.</param>
/// <param name="LinkText">The link's text, such as Fix in Settings &gt; Detection; null with <paramref name="Href"/>.</param>
/// <param name="Detail">The tool's own message, without memory addresses or the file's path; null when the summary holds all of it.</param>
public sealed partial record DetectionFailureExplanation(string Summary, string Advice, string? Href, string? LinkText, string? Detail)
{
    private const string UnreadableAdvice = "The file may be damaged, incomplete or not a video. Check that it plays, then run Detect Now.";

    /// <summary>The explanation of <paramref name="error"/>, the error a detection of <paramref name="path"/> stored.</summary>
    public static DetectionFailureExplanation FromError(string error, string? path = null)
    {
        if (FfprobeExited().Match(error) is { Success: true } ffprobeExited)
        {
            return new("ffprobe could not read the file.", UnreadableAdvice, null, null, Clean(ffprobeExited.Groups["message"].Value, path));
        }

        if (FfmpegExited().Match(error) is { Success: true } ffmpegExited)
        {
            var position = TimeSpan.FromSeconds(double.Parse(ffmpegExited.Groups["seconds"].Value, CultureInfo.InvariantCulture));
            return new(
                $"ffmpeg could not read the picture {position:h\\:mm\\:ss} into the file.",
                "The file may be damaged at that point. Check that it plays past it, then run Detect Now.",
                null,
                null,
                Clean(ffmpegExited.Groups["message"].Value, path));
        }

        if (TimedOut().Match(error) is { Success: true } timedOut)
        {
            return new(
                $"Detection took longer than its {timedOut.Groups["seconds"].Value} s timeout, after {timedOut.Groups["taken"].Value} of {timedOut.Groups["count"].Value} samples.",
                "Raise the timeout or lower the sample count, then run Detect Now.",
                "settings/detection",
                "Fix in Settings > Detection",
                null);
        }

        if (CouldNotRun().Match(error) is { Success: true } couldNotRun)
        {
            return new(
                $"Debarr could not run {couldNotRun.Groups["tool"].Value}.",
                "Check the FFmpeg and FFprobe paths, then run Detect Now.",
                "settings/general",
                "Fix in Settings > General",
                Clean(couldNotRun.Groups["message"].Value, path));
        }

        if (NoCrop().Match(error) is { Success: true } noCrop)
        {
            return new(
                $"None of the {noCrop.Groups["count"].Value} samples found the edges of the picture.",
                "The picture may be dark or blank at every sample. Set the ratio with an override.",
                null,
                null,
                null);
        }

        if (error.StartsWith("ffprobe ", StringComparison.Ordinal))
        {
            return new("ffprobe could not read the file.", UnreadableAdvice, null, null, Clean(error, path));
        }

        return new("Detection failed.", "System > Logs has the details.", "system/logs", "Open System > Logs", Clean(error, path));
    }

    /// <summary>The summary, the advice and the detail in one line, for a tooltip.</summary>
    public string ToTooltipText() => Detail is null ? $"{Summary} {Advice}" : $"{Summary} {Advice} {Detail}";

    /// <summary>
    /// Each line of a tool's message as a sentence, without the memory address ffmpeg puts in its log prefix or the path it names the input by.
    /// Null when nothing is left.
    /// </summary>
    private static string? Clean(string message, string? path)
    {
        var sentences = message
            .Split(["\r\n", "\n", " | "], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(line => LogPrefix().Replace(line, ""))
            .Select(line => path is not null && line.StartsWith($"{path}: ", StringComparison.Ordinal) ? line[(path.Length + 2)..] : line)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line => line[^1] is '.' or '!' or '?' ? line : $"{line}.")
            .Distinct()
            .ToList();
        return sentences.Count == 0 ? null : string.Join(" ", sentences);
    }

    [GeneratedRegex(@"^ffprobe exited -?\d+: (?<message>.*)$", RegexOptions.Singleline)]
    private static partial Regex FfprobeExited();

    [GeneratedRegex(@"^ffmpeg exited -?\d+ sampling at (?<seconds>[\d.]+)s: (?<message>.*)$", RegexOptions.Singleline)]
    private static partial Regex FfmpegExited();

    [GeneratedRegex(@"^timed out after (?<seconds>\d+)s \((?<taken>\d+)/(?<count>\d+) samples\)")]
    private static partial Regex TimedOut();

    [GeneratedRegex(@"^(?:could not run (?<tool>ffmpeg or ffprobe|ffmpeg|ffprobe): |(?<tool>ffmpeg|ffprobe) -version exited -?\d+: )(?<message>.*)$", RegexOptions.Singleline)]
    private static partial Regex CouldNotRun();

    [GeneratedRegex(@"^no crop detected in (?<count>\d+) samples")]
    private static partial Regex NoCrop();

    // Such as "[matroska,webm @ 000001711537dec0] ".
    [GeneratedRegex(@"\[[^\]]* @ (?:0x)?[0-9a-fA-F]+\]\s*")]
    private static partial Regex LogPrefix();
}
