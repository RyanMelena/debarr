using Debarr.Detecting;
using Fisher;
using Wolverine.Fisher;

namespace Debarr.Scanning;

/// <summary>Unarchives the stream of a video file a scan found again, so the restore that follows can append to it.</summary>
public sealed record UnarchiveVideoFile(FileHash VideoFile);

public static class UnarchiveVideoFileHandler
{
    /// <summary>Unarchives the stream and its events, in a commit of its own, since the event store refuses an append in the session that unarchives.</summary>
    public static IFisherOp Handle(UnarchiveVideoFile command) => new UnarchiveStream(command.VideoFile.StreamId);

    private sealed record UnarchiveStream(Guid StreamId) : IFisherOp
    {
        public void Execute(IDocumentSession session) => session.Events.UnArchiveStream(StreamId);
    }
}
