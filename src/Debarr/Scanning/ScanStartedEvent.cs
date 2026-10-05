using Debarr.Activity;

namespace Debarr.Scanning;

/// <summary>A library or folder scan started.</summary>
/// <param name="Folder">The folder of a folder scan; null for a library scan.</param>
public sealed record ScanStartedEvent(string? Folder) : ActivityEvent;
