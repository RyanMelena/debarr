using Debarr.Activity;

namespace Debarr.Detecting;

/// <summary>An activity event about one video file.</summary>
public abstract record VideoFileEvent(FileHash VideoFile) : ActivityEvent;
