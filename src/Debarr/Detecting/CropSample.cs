namespace Debarr.Detecting;

/// <summary>One cropdetect run at a position in the file.</summary>
/// <param name="Position">Where the one-second sample window starts.</param>
/// <param name="Box">Null when ffmpeg reported no picture area, such as for an all-black window.</param>
public sealed record CropSample(TimeSpan Position, CropBox? Box);
