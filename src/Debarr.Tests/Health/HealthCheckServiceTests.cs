using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Debarr.Activity;
using Debarr.Health;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

namespace Debarr.Tests.Health;

public sealed class HealthCheckServiceTests : IDisposable
{
    private static readonly HealthMessage Warning = new(HealthSeverity.Warning, "A warning.", "settings/library", "Fix in Settings > Library");
    private static readonly HealthMessage Error = new(HealthSeverity.Error, "An error.", "settings/players", "Fix in Settings > Players");

    private readonly TestScheduler _scheduler = new();
    private readonly StubHealthCheck _first = new(HealthCheckKind.RootFolder);
    private readonly StubHealthCheck _second = new(HealthCheckKind.PlayerConnection);
    private readonly HealthCheckService _service;
    private readonly List<IReadOnlyList<HealthMessage>> _messages = [];
    private readonly List<ActivityEvent> _activityEvents = [];

    public HealthCheckServiceTests() =>
        _service = new HealthCheckService([_first, _second], _scheduler, NullLogger<HealthCheckService>.Instance);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task Startup_runs_every_check_and_lists_errors_first()
    {
        _first.Result = [Warning];
        _second.Result = [Error];

        await StartAsync();
        await Poll.UntilAsync(() => _messages[^1].Count == 2);

        Assert.Equal([Error, Warning], _messages[^1]);
        Assert.Equal((1, 1), (_first.Runs, _second.Runs));
    }

    [Fact]
    public async Task A_burst_of_triggers_runs_its_check_once_and_a_changed_result_reports_a_health_changed_event()
    {
        await StartAsync();
        _first.Result = [Warning];

        _first.Trigger();
        _first.Trigger();
        _first.Trigger();
        _scheduler.AdvanceBy(HealthCheckService.TriggerInterval.Ticks);
        await Poll.UntilAsync(() => _messages[^1].Count == 1);

        Assert.Equal((2, 1), (_first.Runs, _second.Runs));
        Assert.Equal([Warning], _messages[^1]);
        Assert.IsType<HealthChangedEvent>(Assert.Single(_activityEvents));
    }

    [Fact]
    public async Task An_unchanged_result_reports_nothing()
    {
        _first.Result = [Warning];
        await StartAsync();

        _first.Trigger();
        _scheduler.AdvanceBy(HealthCheckService.TriggerInterval.Ticks);
        await Poll.UntilAsync(() => _first.Runs == 2);

        Assert.Empty(_activityEvents);
    }

    [Fact]
    public async Task A_check_that_throws_is_an_error_that_links_to_the_logs()
    {
        _first.Failure = new InvalidOperationException("The database is unavailable.");

        await StartAsync();
        await Poll.UntilAsync(() => _messages[^1].Count == 1);

        var message = Assert.Single(_messages[^1]);
        Assert.Equal(
            (HealthSeverity.Error, "A health check failed, so a problem may be missing here: The database is unavailable.", "system/logs"),
            (message.Severity, message.Text, message.Href));
    }

    [Fact]
    public async Task Messages_of_one_severity_list_in_the_order_of_their_checks_kinds_whatever_the_order_the_checks_come_in()
    {
        StubHealthCheck[] checks =
        [
            new(HealthCheckKind.Detection) { Result = [Warning with { Text = "Files failed detection." }] },
            new(HealthCheckKind.Notifier) { Result = [Warning with { Text = "A notifier is failing." }] },
            new(HealthCheckKind.RootFolder) { Result = [Warning with { Text = "A root folder is missing." }] },
            new(HealthCheckKind.PlayerConnection) { Result = [Warning with { Text = "A player is disconnected." }] },
            new(HealthCheckKind.Ffmpeg) { Result = [Warning with { Text = "ffmpeg is missing." }] },
        ];
        using var service = new HealthCheckService(checks, _scheduler, NullLogger<HealthCheckService>.Instance);
        List<IReadOnlyList<HealthMessage>> messages = [];

        await service.StartAsync(CancellationToken);
        using var subscription = service.Messages.Subscribe(messages.Add);
        await Poll.UntilAsync(() => messages.Count > 0 && messages[^1].Count == checks.Length);

        Assert.Equal(
            ["A root folder is missing.", "ffmpeg is missing.", "A player is disconnected.", "A notifier is failing.", "Files failed detection."],
            messages[^1].Select(message => message.Text));
    }

    private async Task StartAsync()
    {
        await _service.StartAsync(CancellationToken);
        _service.Messages.Subscribe(_messages.Add);
        _service.ActivityEvents.Subscribe(_activityEvents.Add);
        await Poll.UntilAsync(() => _first.Runs == 1 && _second.Runs == 1);
    }

    /// <summary>A check whose result the test sets and whose trigger the test fires.</summary>
    private sealed class StubHealthCheck(HealthCheckKind kind) : IHealthCheck
    {
        private readonly Subject<Unit> _triggers = new();
        private int _runs;

        public IReadOnlyList<HealthMessage> Result { get; set; } = [];

        public Exception? Failure { get; set; }

        public int Runs => Volatile.Read(ref _runs);

        public HealthCheckKind Kind => kind;

        public IObservable<Unit> Triggers => _triggers.AsObservable();

        public void Trigger() => _triggers.OnNext(Unit.Default);

        public Task<IReadOnlyList<HealthMessage>> CheckAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _runs);
            return Failure is { } failure ? Task.FromException<IReadOnlyList<HealthMessage>>(failure) : Task.FromResult(Result);
        }
    }
}
