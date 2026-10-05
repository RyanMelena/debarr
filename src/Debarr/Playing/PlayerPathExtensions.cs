using Debarr.Scanning;

namespace Debarr.Playing;

public static class PlayerPathExtensions
{
    private static readonly string[] StreamSchemes = ["plugin://", "pvr://"];

    /// <summary>Whether the path is a stream, such as an add-on or live TV, which has no file to read.</summary>
    public static bool IsStream(this PlayerPath path) =>
        StreamSchemes.Any(scheme => path.Value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase));

    /// <summary>Translated by the path mapping whose player path covers the most whole segments of it, or unchanged when none covers it.</summary>
    public static LocalPath ToLocalPath(this PlayerPath path, IEnumerable<PathMapping> pathMappings) =>
        pathMappings
            .OrderByDescending(pathMapping => pathMapping.PlayerPath.Value.Length)
            .Select(pathMapping => pathMapping.Translate(path))
            .FirstOrDefault(localPath => localPath is not null)
        ?? new LocalPath(path.Value);
}
