using Debarr.Activity;

namespace Debarr.Scanning;

/// <summary>A scan pause is stopping the running library or folder scan, which runs again once the pause ends.</summary>
/// <param name="Folder">The folder of a folder scan; null for a library scan.</param>
public sealed record ScanStoppingEvent(string? Folder) : ActivityEvent;
