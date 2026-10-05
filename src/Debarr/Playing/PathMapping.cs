using Debarr.Scanning;

namespace Debarr.Playing;

/// <summary>Translates a player path that starts with <see cref="PlayerPath"/> into a local path that starts with <see cref="LocalPath"/>.</summary>
/// <param name="PlayerPath">Canonical under the player type's rules, so it matches the player paths it translates by whole segments.</param>
public sealed record PathMapping(PlayerPath PlayerPath, LocalPath LocalPath)
{
    /// <summary>
    /// The path translated, with its segments joined by the local directory separator,
    /// or null when <see cref="PlayerPath"/> doesn't cover it by whole segments.
    /// </summary>
    public LocalPath? Translate(PlayerPath path)
    {
        // A player path of "/" trims to "", which covers every absolute path.
        var prefix = PlayerPath.Value.TrimEnd('/');
        if (!path.Value.StartsWith(prefix, StringComparison.Ordinal)
            || (path.Value.Length > prefix.Length && path.Value[prefix.Length] != '/'))
        {
            return null;
        }

        return new LocalPath(LocalPath.Value.TrimEnd('/', Path.DirectorySeparatorChar) + path.Value[prefix.Length..].Replace('/', Path.DirectorySeparatorChar));
    }
}
