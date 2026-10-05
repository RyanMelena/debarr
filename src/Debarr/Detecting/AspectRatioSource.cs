namespace Debarr.Detecting;

/// <summary>Where a detection result's ratio came from: the container's ratio, or cropdetect's measurement of the picture.</summary>
public enum AspectRatioSource
{
    FromFile,
    Detected,
}
