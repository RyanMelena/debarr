using Debarr.Activity;

namespace Debarr.Scanning;

/// <summary>The removal of a root folder's file paths ended.</summary>
/// <param name="Error">The failure's message when the removal failed.</param>
public sealed record RootFolderRemovalFinishedEvent(string RootFolder, string? Error) : ActivityEvent;
