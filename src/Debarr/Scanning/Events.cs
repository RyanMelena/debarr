namespace Debarr.Scanning;

public sealed record RootFolderAdded(LocalPath Path, DateTimeOffset AddedAt);

public sealed record RootFolderEnabled(LocalPath Path);

public sealed record RootFolderDisabled(LocalPath Path);

public sealed record RootFolderRemoved(LocalPath Path);

public sealed record LibrarySettingsChanged(LibrarySettings Settings);

public sealed record LibraryScanStarted(DateTimeOffset StartedAt);

/// <summary>The library scan covered a root folder, as that root folder finished.</summary>
/// <param name="StartedAt">When the library scan started.</param>
/// <param name="Error">The scan's error, such as a missing folder; null when it read the root folder.</param>
public sealed record RootFolderScanned(LocalPath Path, DateTimeOffset StartedAt, string? Error);

/// <summary>The library scan finished, failed or was cancelled.</summary>
/// <param name="Counts">What the part of the scan that ran did.</param>
public sealed record LibraryScanEnded(DateTimeOffset EndedAt, LibraryScanCounts Counts, LibraryScanOutcome Outcome);

/// <summary>Startup found the library scan open after the process stopped before its end.</summary>
public sealed record LibraryScanInterrupted(DateTimeOffset NoticedAt);
