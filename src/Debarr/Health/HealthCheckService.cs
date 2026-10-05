using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using Debarr.Activity;

namespace Debarr.Health;

/// <summary>
/// Runs every health check at startup and again on each of its triggers, from startup to shutdown, and holds the messages they find.
/// A check's newer trigger cancels its run in progress, and each check runs apart from the others.
/// </summary>
public sealed partial class HealthCheckService : IHostedService, IActivitySource, IDisposable
{
    /// <summary>The most often one check runs, so a burst of triggers, such as a scan hashing files, costs one run a second.</summary>
    public static readonly TimeSpan TriggerInterval = TimeSpan.FromSeconds(1);

    private static readonly IEqualityComparer<IReadOnlyList<HealthMessage>> SameMessages =
        EqualityComparer<IReadOnlyList<HealthMessage>>.Create((left, right) => left!.SequenceEqual(right!), messages => messages.Count);

    private readonly ILogger<HealthCheckService> _logger;

    private readonly IConnectableObservable<IReadOnlyList<HealthMessage>> _messages;

    private readonly CompositeDisposable _disposables = [];

    public HealthCheckService(IEnumerable<IHealthCheck> healthChecks, IScheduler scheduler, ILogger<HealthCheckService> logger)
    {
        _logger = logger;
        _messages = healthChecks
            .OrderBy(healthCheck => healthCheck.Kind)
            .Select(healthCheck => healthCheck.Triggers
                .Sample(TriggerInterval, scheduler)
                .StartWith(Unit.Default)
                .Select(_ => Observable.FromAsync(cancellationToken => CheckAsync(healthCheck, cancellationToken)))
                .Switch()
                .StartWith([]))
            // Emits an empty list when no check is registered.
            .Append(Observable.Return<IReadOnlyList<HealthMessage>>([]))
            .CombineLatest()
            .Select(IReadOnlyList<HealthMessage> (results) => [.. results.SelectMany(messages => messages).OrderByDescending(message => message.Severity)])
            .DistinctUntilChanged(SameMessages)
            .Replay(1);
    }

    /// <summary>The messages every check found, errors first and then by their check's kind, replayed at once to each subscriber and emitted again when they change.</summary>
    public IObservable<IReadOnlyList<HealthMessage>> Messages => _messages;

    /// <summary>A <see cref="HealthChangedEvent"/> each time the messages change after the subscription starts.</summary>
    public IObservable<ActivityEvent> ActivityEvents => _messages.Skip(1).Select(ActivityEvent (_) => new HealthChangedEvent());

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _messages.Connect().DisposeWith(_disposables);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _disposables.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => _disposables.Dispose();

    /// <summary>The check's messages, or one error message naming the failure when the check throws.</summary>
    private async Task<IReadOnlyList<HealthMessage>> CheckAsync(IHealthCheck healthCheck, CancellationToken cancellationToken)
    {
        try
        {
            return await healthCheck.CheckAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogHealthCheckFailed(exception, healthCheck.GetType().Name);
            return [new HealthMessage(HealthSeverity.Error, $"A health check failed, so a problem may be missing here: {exception.Message}", "system/logs", "Open System > Logs")];
        }
    }

    [LoggerMessage(LogLevel.Error, "The health check {HealthCheck} failed.")]
    private partial void LogHealthCheckFailed(Exception exception, string healthCheck);
}
