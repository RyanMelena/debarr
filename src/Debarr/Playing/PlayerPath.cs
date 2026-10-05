namespace Debarr.Playing;

/// <summary>
/// The path of a file a player is playing, in the player's terms, which its path mappings translate into a local path.
/// The value is canonical under the player type's rules, so a player path matches it by whole segments.
/// </summary>
public readonly record struct PlayerPath(string Value);
