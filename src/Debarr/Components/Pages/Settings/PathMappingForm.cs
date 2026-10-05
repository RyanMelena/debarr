using Debarr.Playing;

namespace Debarr.Components.Pages.Settings;

/// <summary>One path mapping as a player modal edits it.</summary>
public sealed class PathMappingForm
{
    public string PlayerPath { get; set; } = "";

    public string LocalPath { get; set; } = "";

    public static PathMappingForm FromPathMapping(PathMapping pathMapping) => new()
    {
        PlayerPath = pathMapping.PlayerPath.Value,
        LocalPath = pathMapping.LocalPath.Value,
    };

    public PathMappingForm Copy() => new() { PlayerPath = PlayerPath, LocalPath = LocalPath };
}
