namespace Debarr.Detecting;

public static class AspectRatioSourceExtensions
{
    /// <summary>A detection result's source, as the UI words it after the ratio, such as "from file".</summary>
    public static string ToDisplayText(this AspectRatioSource source) => source switch
    {
        AspectRatioSource.FromFile => "from file",
        AspectRatioSource.Detected => "detected",
        _ => "",
    };
}
