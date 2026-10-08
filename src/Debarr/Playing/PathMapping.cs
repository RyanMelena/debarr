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
        if (!PlayerPath.Covers(path))
        {
            return null;
        }

        var prefix = PlayerPath.Value.TrimEnd('/');
        return new LocalPath(LocalPath.Value.TrimEnd('/', Path.DirectorySeparatorChar) + path.Value[prefix.Length..].Replace('/', Path.DirectorySeparatorChar));
    }
}
