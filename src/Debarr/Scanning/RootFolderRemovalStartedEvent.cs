using Debarr.Activity;

namespace Debarr.Scanning;

/// <summary>The removal of a root folder's file paths started, which archives the video files they leave with none.</summary>
public sealed record RootFolderRemovalStartedEvent(string RootFolder, DateTimeOffset StartedAt) : ActivityEvent;
