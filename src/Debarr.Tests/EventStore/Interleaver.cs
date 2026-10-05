using Fisher;
using Fisher.Services;
using FluentResults;
using IDocumentSessionOperations = JasperFx.Events.Documents.IDocumentSessionOperations;

namespace Debarr.Tests.EventStore;

/// <summary>Commits another command once, while a session that matches is about to write.</summary>
public sealed class Interleaver : IDocumentSessionListener
{
    private Func<IDocumentSessionOperations, bool>? _matches;
    private Func<Task<Result>>? _interleave;

    /// <summary>Interleaves before the write of a session holding a pending event of type <typeparamref name="TEvent"/>.</summary>
    public void Before<TEvent>(Func<Task<Result>> interleave) =>
        Before(session => session.PendingStreams.SelectMany(stream => stream.Events).Any(pending => pending.Data is TEvent), interleave);

    /// <summary>Interleaves before the write of a session that <paramref name="matches"/>.</summary>
    public void Before(Func<IDocumentSessionOperations, bool> matches, Func<Task<Result>> interleave)
    {
        _matches = matches;
        _interleave = interleave;
    }

    public async Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
    {
        if (_interleave is { } interleave && _matches!((IDocumentSessionOperations)session))
        {
            _interleave = null;
            Assert.True((await interleave()).IsSuccess);
        }
    }

    public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token) => Task.CompletedTask;
}
