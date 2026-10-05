using Debarr.Activity;

namespace Debarr.Scanning;

/// <summary>A library or folder scan ended.</summary>
/// <param name="Folder">The folder of a folder scan; null for a library scan.</param>
/// <param name="Error">The failure's message when the scan failed.</param>
/// <param name="Cancelled">True when a scan pause stopped the scan, which runs again once the pause ends.</param>
public sealed record ScanFinishedEvent(string? Folder, TimeSpan Duration, string? Error, bool Cancelled) : ActivityEvent;
