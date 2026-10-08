using Debarr.Playing;

namespace Debarr.Components.Pages.Settings;

/// <summary>One excluded path as a player modal edits it.</summary>
public sealed class ExcludedPathForm
{
    public string PlayerPath { get; set; } = "";

    public static ExcludedPathForm FromPlayerPath(PlayerPath playerPath) => new() { PlayerPath = playerPath.Value };

    public ExcludedPathForm Copy() => new() { PlayerPath = PlayerPath };
}
