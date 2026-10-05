using Wolverine.Persistence.EventSourcing;

namespace Debarr.Scanning;

/// <summary>Ends a library scan that startup found open after the process stopped before its end.</summary>
public sealed record InterruptLibraryScan(Guid LibraryScanId, DateTimeOffset NoticedAt);

public static class InterruptLibraryScanHandler
{
    /// <summary>Interrupts the scan while it is open.</summary>
    public static IReadOnlyList<object> Handle(InterruptLibraryScan command, [WriteModel(Required = false)] LibraryScan? libraryScan) =>
        libraryScan is { Ended: false } ? [new LibraryScanInterrupted(command.NoticedAt)] : [];
}
