using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using Debarr.Activity;
using Fisher;
using Fisher.Projections;
using Fisher.Services;
using JasperFx.Events;
using JasperFx.Events.Projections;

namespace Debarr.EventStore;

/// <summary>
/// Publishes each event a commit appended as a <see cref="CommittedEvent"/>, and then a <see cref="ReadModelChanged"/> for every read model one of the commit's events changes:
/// a projection that handles the event, named for its read model, or an aggregate the pages fold whose <c>Create</c> or <c>Apply</c> takes it, named for the aggregate.
/// </summary>
public sealed partial class ReadModelChangeListener(
    FisherProjectionOptions projections,
    IReadOnlyList<Type> foldedAggregates,
    ILogger<ReadModelChangeListener> logger) : IDocumentSessionListener, IActivitySource
{
    // Commits end on their sessions' threads, so the subject serializes their events.
    private readonly ISubject<ActivityEvent> _subject = Subject.Synchronize(new Subject<ActivityEvent>());

    // The projections are complete once the store is built, which is before the first commit.
    private readonly Lazy<IReadOnlyList<(string Name, IReadOnlyList<Type> EventTypes)>> _readModels = new(() =>
    [
        .. projections.All.OfType<ProjectionBase>().Select(projection => (projection.Name, (IReadOnlyList<Type>)[.. projection.IncludedEventTypes])),
        .. foldedAggregates.Select(aggregate => (aggregate.Name, EventTypesOf(aggregate))),
    ]);

    public IObservable<ActivityEvent> ActivityEvents => _subject.AsObservable();

    public IObservable<ReadModelChanged> ChangesTo(params IReadOnlyCollection<string> readModels) =>
        _subject.OfType<ReadModelChanged>().Where(change => readModels.Contains(change.ReadModel));

    /// <summary>The data of each committed event of type <typeparamref name="TEvent"/>, in the order its commit appended them.</summary>
    public IObservable<TEvent> Committed<TEvent>() =>
        _subject.OfType<CommittedEvent>().Select(committed => committed.Event.Data).OfType<TEvent>();

    public Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token) => Task.CompletedTask;

    public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        using var subscribersFlow = SeparateSubscribersFromTheCommandsLogScope();

        var events = commit.GetEvents().ToList();
        foreach (var committed in events)
        {
            Publish(new CommittedEvent(committed));
        }

        foreach (var (name, eventTypes) in _readModels.Value)
        {
            var streams = events
                .Where(committed => eventTypes.Any(type => type.IsAssignableFrom(committed.EventType)))
                .Select(committed => committed.StreamKey ?? committed.StreamId.ToString())
                .ToHashSet();
            if (streams.Count > 0)
            {
                Publish(new ReadModelChanged(name, streams));
            }
        }

        return Task.CompletedTask;
    }

    // Fisher passes an exception from here to the caller of SaveChangesAsync after the commit, which would fail the command that committed.
    private void Publish(ActivityEvent activityEvent)
    {
        try
        {
            _subject.OnNext(activityEvent);
        }
        catch (Exception exception)
        {
            LogSubscriberFailed(exception);
        }
    }

    /// <summary>
    /// Stops the work a subscriber starts from flowing the committing command's log scope;
    /// null when a subscriber's own commit runs with the flow stopped already.
    /// </summary>
    private static AsyncFlowControl? SeparateSubscribersFromTheCommandsLogScope() =>
        ExecutionContext.IsFlowSuppressed() ? null : ExecutionContext.SuppressFlow();

    private static IReadOnlyList<Type> EventTypesOf(Type aggregate) =>
    [
        .. aggregate.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Where(method => method.Name is "Create" or "Apply" && method.GetParameters().Length > 0)
            .Select(method => method.GetParameters()[0].ParameterType)
            .Select(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEvent<>) ? type.GetGenericArguments()[0] : type)
            .Distinct(),
    ];

    [LoggerMessage(LogLevel.Error, "A subscriber to the commits failed.")]
    private partial void LogSubscriberFailed(Exception exception);
}
