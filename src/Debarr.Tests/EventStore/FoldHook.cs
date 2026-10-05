using Fisher;
using Fisher.Projections;
using FluentResults;
using JasperFx.Events;

namespace Debarr.Tests.EventStore;

/// <summary>
/// Runs test code once, after the inline projections registered before it folded an event of a type and before the session writes,
/// so a test can commit another write between a fold and its write.
/// </summary>
public sealed class FoldHook : EventProjection
{
    private Func<IEvent, bool>? _matches;
    private Func<Task<Result>>? _interleave;

    public FoldHook() => Name = nameof(FoldHook);

    public void Once<TEvent>(Func<Task<Result>> interleave)
    {
        _matches = @event => @event.Data is TEvent;
        _interleave = interleave;
    }

    public override async ValueTask ApplyAsync(IDocumentSession operations, IEvent @event, CancellationToken cancellation)
    {
        if (_interleave is { } interleave && _matches!(@event))
        {
            _interleave = null;
            Assert.True((await interleave()).IsSuccess, "the interleaved command should commit");
        }
    }
}
