using System.Text.Json.Serialization;
using JasperFx.Events;

namespace Debarr.Scanning;

/// <summary>One run of the library scan, on a stream of its own.</summary>
/// <param name="Ended">True once the scan ended or was interrupted.</param>
public sealed record LibraryScan(Guid Id, DateTimeOffset StartedAt, bool Ended)
{
    public static LibraryScan Create(IEvent<LibraryScanStarted> started) => new(started.StreamId, started.Data.StartedAt, false);

    public LibraryScan Apply(RootFolderScanned scanned) => this;

    public LibraryScan Apply(LibraryScanEnded ended) => this with { Ended = true };

    public LibraryScan Apply(LibraryScanInterrupted interrupted) => this with { Ended = true };
}

/// <summary>How a library scan ended.</summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(Finished), "finished")]
[JsonDerivedType(typeof(Failed), "failed")]
[JsonDerivedType(typeof(Cancelled), "cancelled")]
[JsonDerivedType(typeof(Interrupted), "interrupted")]
public abstract record LibraryScanOutcome
{
    private LibraryScanOutcome()
    {
    }

    /// <summary>The scan covered every enabled root folder and archived the video files left with no path.</summary>
    public sealed record Finished : LibraryScanOutcome;

    /// <param name="Error">Why the scan failed.</param>
    public sealed record Failed(string Error) : LibraryScanOutcome;

    /// <summary>Debarr shut down or a scan pause stopped the scan, which ended with the counts of the part that ran.</summary>
    public sealed record Cancelled : LibraryScanOutcome;

    /// <summary>The process stopped before the scan ended, and startup ended it with no counts.</summary>
    public sealed record Interrupted : LibraryScanOutcome;
}

/// <summary>How many file paths a library scan found, files it hashed, video files it added, file paths it removed and video files it archived.</summary>
public sealed record LibraryScanCounts(int FilePathsFound, int FilesHashed, int VideoFilesAdded, int FilePathsDeleted, int VideoFilesArchived);
