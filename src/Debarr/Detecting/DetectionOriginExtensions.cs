namespace Debarr.Detecting;

public static class DetectionOriginExtensions
{
    /// <summary>What started a detection, as the UI words it, such as "Detect Now".</summary>
    public static string ToDisplayText(this DetectionOrigin origin) => origin switch
    {
        DetectionOrigin.Queue => "Queue",
        DetectionOrigin.DetectNow => "Detect Now",
        DetectionOrigin.StandardRatiosChange => "Standard Ratios Change",
        _ => "",
    };
}
