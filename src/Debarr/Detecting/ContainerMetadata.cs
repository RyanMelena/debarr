namespace Debarr.Detecting;

/// <summary>What ffprobe reports about the first video stream, as a detection records it.</summary>
/// <param name="ContainerAspectRatio">The display ratio of the full frame, with non-square pixels applied.</param>
public sealed record ContainerMetadata(
    AspectRatio ContainerAspectRatio,
    int Width,
    int Height,
    string CodecName,
    string? ColorTransfer)
{
    /// <summary>True for PQ (smpte2084) and HLG (arib-std-b67).</summary>
    public bool IsHdr => ColorTransfer is "smpte2084" or "arib-std-b67";
}
