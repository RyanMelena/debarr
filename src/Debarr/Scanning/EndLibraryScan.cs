using Wolverine.Persistence.EventSourcing;

namespace Debarr.Scanning;

/// <summary>Ends a library scan that finished, failed or was cancelled.</summary>
/// <param name="Counts">What the part of the scan that ran did.</param>
public sealed record EndLibraryScan(Guid LibraryScanId, DateTimeOffset EndedAt, LibraryScanCounts Counts, LibraryScanOutcome Outcome);

public static class EndLibraryScanHandler
{
    /// <summary>Ends the scan while it is open.</summary>
    public static IReadOnlyList<object> Handle(EndLibraryScan command, [WriteModel(Required = false)] LibraryScan? libraryScan) =>
        libraryScan is { Ended: false } ? [new LibraryScanEnded(command.EndedAt, command.Counts, command.Outcome)] : [];
}
