namespace Debarr.Playing;

/// <summary>
/// The path of a file a player is playing, in the player's terms, which its path mappings translate into a local path.
/// The value is canonical under the player type's rules, so a player path matches it by whole segments.
/// </summary>
public readonly record struct PlayerPath(string Value)
{
    /// <summary>Whether <paramref name="path"/> starts with this path by whole segments.</summary>
    public bool Covers(PlayerPath path)
    {
        // A player path of "/" trims to "", which covers every absolute path.
        var prefix = Value.TrimEnd('/');
        return path.Value.StartsWith(prefix, StringComparison.Ordinal)
            && (path.Value.Length == prefix.Length || path.Value[prefix.Length] == '/');
    }
}
