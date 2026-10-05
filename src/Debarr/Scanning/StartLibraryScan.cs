using Wolverine.Fisher;

namespace Debarr.Scanning;

/// <param name="LibraryScanId">The new library scan's stream.</param>
public sealed record StartLibraryScan(Guid LibraryScanId, DateTimeOffset StartedAt);

public static class StartLibraryScanHandler
{
    public static StartStream<LibraryScan> Handle(StartLibraryScan command) =>
        FisherOps.StartStream<LibraryScan>(command.LibraryScanId, new LibraryScanStarted(command.StartedAt));
}
