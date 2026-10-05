namespace Debarr.Detecting;

/// <summary>Where a video file stands, from its override, then its current result, then its last failure.</summary>
public enum VideoFileStatus
{
    Pending,
    Failed,
    FromFile,
    Detected,
    Manual,
}
