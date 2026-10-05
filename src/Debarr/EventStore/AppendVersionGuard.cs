using Fisher;
using Fisher.Services;
using JasperFx.Events;
using IDocumentSessionOperations = JasperFx.Events.Documents.IDocumentSessionOperations;

namespace Debarr.EventStore;

/// <summary>
/// Refuses a commit that appends to a stream without stating the version the append expects, 0 for a stream not yet started,
/// before the commit's inline projections fold it, since the event store checks only a stated version under its write lock.
/// </summary>
public sealed class AppendVersionGuard : IDocumentSessionListener
{
    public Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
    {
        if (((IDocumentSessionOperations)session).PendingStreams.FirstOrDefault(stream => stream is { ActionType: StreamActionType.Append, ExpectedVersionOnServer: null, Events.Count: > 0 }) is { } unversioned)
        {
            throw new InvalidOperationException($"An append to stream {unversioned.Id} states no expected version.");
        }

        return Task.CompletedTask;
    }

    public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token) => Task.CompletedTask;
}
