namespace Debarr.Hosting;

/// <summary>The app's host settings, bound from DEBARR__APP__: the data directory.</summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    public string DataDir { get; set; } = "/data";
}
