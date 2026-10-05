namespace Debarr.Scanning;

/// <summary>A path on Debarr's filesystem, such as a root folder, a file path or a translated player path.</summary>
public readonly record struct LocalPath(string Value);
