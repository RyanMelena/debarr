using System.Text.Json.Serialization;
using FluentResults;

namespace Debarr.Scanning;

/// <summary>The extensions that make a file a video file: lowercase with no leading dot, in first-seen order and without duplicates.</summary>
public sealed class VideoExtensions : IEquatable<VideoExtensions>
{
    [JsonConstructor]
    private VideoExtensions(IReadOnlyList<string> extensions) => Extensions = extensions;

    public static VideoExtensions Default { get; } = Parse("mkv mp4 m4v avi mov ts m2ts wmv webm mpg mpeg").Value;

    public IReadOnlyList<string> Extensions { get; }

    /// <summary>The extensions separated by spaces or commas, normalised, or a field error when none is left.</summary>
    public static Result<VideoExtensions> Parse(string text)
    {
        var extensions = text
            .Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(extension => extension.TrimStart('.').ToLowerInvariant())
            .Where(extension => extension.Length > 0)
            .Distinct()
            .ToList();

        return extensions.Count == 0
            ? Result.Fail(new FieldError(nameof(VideoExtensions), "Enter at least one extension."))
            : new VideoExtensions(extensions);
    }

    /// <summary>Whether the file's extension, without its dot, is one of these, ignoring case.</summary>
    public bool Includes(FileInfo file) =>
        Extensions.Contains(file.Extension.TrimStart('.'), StringComparer.OrdinalIgnoreCase);

    public bool Equals(VideoExtensions? other) => other is not null && Extensions.SequenceEqual(other.Extensions);

    public override bool Equals(object? obj) => Equals(obj as VideoExtensions);

    public override int GetHashCode() => ToString().GetHashCode(StringComparison.Ordinal);

    /// <summary>The extensions separated by spaces, as the form holds them.</summary>
    public override string ToString() => string.Join(' ', Extensions);
}
