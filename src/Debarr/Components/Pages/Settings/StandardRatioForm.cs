using Debarr.Detecting;

namespace Debarr.Components.Pages.Settings;

/// <summary>One standard ratio as Settings &gt; Detection edits it.</summary>
public sealed record StandardRatioForm
{
    public double AspectRatio { get; set; }

    public bool ChecksPicture { get; set; }

    public static StandardRatioForm FromStandardRatio(StandardRatio standardRatio) => new()
    {
        AspectRatio = standardRatio.AspectRatio,
        ChecksPicture = standardRatio.ChecksPicture,
    };

    public StandardRatio ToStandardRatio() => new(AspectRatio, ChecksPicture);
}
