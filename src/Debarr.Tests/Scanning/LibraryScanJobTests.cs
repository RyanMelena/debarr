using Debarr.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Quartz;

namespace Debarr.Tests.Scanning;

public sealed class LibraryScanJobTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly TriggerKey ScheduledTriggerKey = new("scheduled", "library-scan");
    private static readonly TriggerKey ImmediateTriggerKey = new("immediate", "library-scan");

    private readonly FakeTimeProvider _timeProvider = new(Start);
    private ServiceProvider _services = null!;
    private IScheduler _scheduler = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _services = new ServiceCollection()
            .AddLogging()
            .AddQuartz(quartz =>
            {
                quartz.UseInMemoryStore();
                quartz.AddScanJobs();
                quartz.UseTimeProvider(_timeProvider);

                // Quartz keeps one scheduler per name in the process, and tests run in parallel.
                quartz.ConfigureScheduler(options => options.InstanceName = Guid.NewGuid().ToString());
            })
            .BuildServiceProvider();
        _scheduler = await _services.GetRequiredService<ISchedulerFactory>().GetScheduler(CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _scheduler.Shutdown(waitForJobsToComplete: false, CancellationToken.None);
        await _services.DisposeAsync();
    }

    [Fact]
    public async Task A_schedule_runs_its_first_scan_one_interval_from_now_and_repeats_at_that_interval()
    {
        await LibraryScanJob.ScheduleAsync(_scheduler, new ScanInterval(12), CancellationToken);

        var trigger = Assert.IsAssignableFrom<ISimpleTrigger>(await _scheduler.GetTrigger(ScheduledTriggerKey, CancellationToken));
        Assert.Equal(Start.AddHours(12), trigger.NextFireTimeUtc);
        Assert.Equal(TimeSpan.FromHours(12), trigger.RepeatInterval);
        Assert.Equal(-1, trigger.RepeatCount);
        Assert.Equal(LibraryScanner.ExecutionGroup, trigger.ExecutionGroup);
        Assert.Equal(SimpleTriggerMisfireInstruction.FireNow, trigger.MisfireInstruction);
    }

    [Fact]
    public async Task A_new_interval_replaces_the_schedule_from_the_time_it_is_saved()
    {
        await LibraryScanJob.ScheduleAsync(_scheduler, new ScanInterval(12), CancellationToken);

        _timeProvider.Advance(TimeSpan.FromHours(3));
        await LibraryScanJob.ScheduleAsync(_scheduler, new ScanInterval(6), CancellationToken);

        var trigger = await _scheduler.GetTrigger(ScheduledTriggerKey, CancellationToken);
        Assert.Equal(Start.AddHours(9), trigger!.NextFireTimeUtc);
    }

    [Fact]
    public async Task No_interval_removes_the_schedule()
    {
        await LibraryScanJob.ScheduleAsync(_scheduler, new ScanInterval(12), CancellationToken);

        await LibraryScanJob.ScheduleAsync(_scheduler, ScanInterval.Off, CancellationToken);

        Assert.Null(await _scheduler.GetTrigger(ScheduledTriggerKey, CancellationToken));
    }

    [Fact]
    public async Task Scan_now_runs_in_the_scan_execution_group()
    {
        await LibraryScanJob.TriggerNowAsync(_scheduler, CancellationToken);

        var trigger = Assert.IsAssignableFrom<ISimpleTrigger>(await _scheduler.GetTrigger(ImmediateTriggerKey, CancellationToken));
        Assert.Equal(LibraryScanner.ExecutionGroup, trigger.ExecutionGroup);
        Assert.Equal(SimpleTriggerMisfireInstruction.FireNow, trigger.MisfireInstruction);
    }

    [Fact]
    public async Task Scan_now_pressed_while_a_library_scan_waits_keeps_the_waiting_one()
    {
        await LibraryScanJob.TriggerNowAsync(_scheduler, CancellationToken);
        var waiting = await _scheduler.GetTrigger(ImmediateTriggerKey, CancellationToken);

        await LibraryScanJob.TriggerNowAsync(_scheduler, CancellationToken);

        var trigger = await _scheduler.GetTrigger(ImmediateTriggerKey, CancellationToken);
        Assert.Equal(waiting!.StartTimeUtc, trigger!.StartTimeUtc);
    }
}
