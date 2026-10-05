namespace Debarr.Hosting;

/// <summary>The paths of ffmpeg and ffprobe, bound from DEBARR__FFMPEG__.</summary>
public sealed class FfmpegOptions
{
    public const string SectionName = "Ffmpeg";

    public string FfmpegPath { get; set; } = "ffmpeg";

    public string FfprobePath { get; set; } = "ffprobe";
}
