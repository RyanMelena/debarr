namespace Debarr.Detecting;

/// <summary>What started a detection: the detection queue, Detect Now, or, for a stored detection, a standard ratios change.</summary>
public enum DetectionOrigin
{
    Queue,
    DetectNow,
    StandardRatiosChange,
}
