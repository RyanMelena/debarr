using System.Collections.Concurrent;
using Debarr.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Debarr.Tests.Scanning;

public sealed class ISchedulerExtensionsTests : IAsyncLifetime
{
    private static readonly JobKey WatchedKey = new("watched", "test");
    private static readonly JobKey OtherKey = new("other", "test");
    private const string FailKey = "fail";

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
                quartz.AddJob<TestJob>(job => job.WithIdentity(WatchedKey).StoreDurably());
                quartz.AddJob<TestJob>(job => job.WithIdentity(OtherKey).StoreDurably());

                // Quartz keeps one scheduler per name in the process, and tests run in parallel.
                quartz.ConfigureScheduler(options => options.InstanceName = Guid.NewGuid().ToString());
            })
            .BuildServiceProvider();
        _scheduler = await _services.GetRequiredService<ISchedulerFactory>().GetScheduler(CancellationToken);
        await _scheduler.Start(CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _scheduler.Shutdown(waitForJobsToComplete: false, CancellationToken.None);
        await _services.DisposeAsync();
    }

    [Fact]
    public async Task A_run_emits_its_start_then_its_end()
    {
        var jobEvents = new ConcurrentQueue<JobEvent>();
        using var subscription = _scheduler.ObserveJobs(Matchers.Key(WatchedKey)).Subscribe(jobEvents.Enqueue);

        await _scheduler.TriggerJob(WatchedKey, cancellationToken: CancellationToken);

        await Poll.UntilAsync(() => jobEvents.OfType<JobFinishedEvent>().Any());
        Assert.Collection(
            jobEvents,
            started => Assert.Equal(WatchedKey, Assert.IsType<JobStartedEvent>(started).Context.JobDetail.Key),
            finished => Assert.Null(Assert.IsType<JobFinishedEvent>(finished).Exception));
    }

    [Fact]
    public async Task A_failed_run_ends_with_its_exception()
    {
        var jobEvents = new ConcurrentQueue<JobEvent>();
        using var subscription = _scheduler.ObserveJobs(Matchers.Key(WatchedKey)).Subscribe(jobEvents.Enqueue);

        await _scheduler.TriggerJob(WatchedKey, new JobDataMap { [FailKey] = true }, CancellationToken);

        await Poll.UntilAsync(() => jobEvents.OfType<JobFinishedEvent>().Any());
        Assert.Equal("The test job failed.", jobEvents.OfType<JobFinishedEvent>().Single().Exception?.GetBaseException().Message);
    }

    [Fact]
    public async Task A_job_the_matcher_leaves_out_emits_nothing()
    {
        var watchedEvents = new ConcurrentQueue<JobEvent>();
        var allEvents = new ConcurrentQueue<JobEvent>();
        using var watched = _scheduler.ObserveJobs(Matchers.Key(WatchedKey)).Subscribe(watchedEvents.Enqueue);
        using var all = _scheduler.ObserveJobs(GroupMatcher<JobKey>.AnyGroup()).Subscribe(allEvents.Enqueue);

        await _scheduler.TriggerJob(OtherKey, cancellationToken: CancellationToken);
        await _scheduler.TriggerJob(WatchedKey, cancellationToken: CancellationToken);

        await Poll.UntilAsync(() => allEvents.OfType<JobFinishedEvent>().Count() == 2);
        Assert.All(watchedEvents, jobEvent => Assert.Equal(WatchedKey, jobEvent.Context.JobDetail.Key));
        Assert.Equal(2, watchedEvents.Count);
    }

    [Fact]
    public void Disposing_a_subscription_removes_its_job_listener()
    {
        var listenersBefore = _scheduler.ListenerManager.GetJobListeners().Count;
        var subscription = _scheduler.ObserveJobs(Matchers.Key(WatchedKey)).Subscribe(_ => { });
        var listenersSubscribed = _scheduler.ListenerManager.GetJobListeners().Count;

        subscription.Dispose();

        Assert.Equal(listenersBefore + 1, listenersSubscribed);
        Assert.Equal(listenersBefore, _scheduler.ListenerManager.GetJobListeners().Count);
    }

    [Fact]
    public async Task Scheduling_replacing_and_unscheduling_a_matched_trigger_each_emit_a_change()
    {
        var watched = new TriggerKey("watched", "test");
        var changes = new ConcurrentQueue<ScheduleChanged>();
        using var subscription = _scheduler.ObserveSchedule(Matchers.Key(watched)).Subscribe(changes.Enqueue);

        await _scheduler.ScheduleJob(LaterTrigger(watched), default, CancellationToken);
        await _scheduler.ScheduleJob(LaterTrigger(new TriggerKey("other", "test")), default, CancellationToken);
        await _scheduler.ScheduleJob(LaterTrigger(watched), ScheduleJobOptions.Replacing, CancellationToken);
        await _scheduler.UnscheduleJob(watched, CancellationToken);

        Assert.Equal(Enumerable.Repeat(new ScheduleChanged(watched), 3), changes);
    }

    [Fact]
    public void Disposing_a_subscription_removes_its_scheduler_listener()
    {
        var listenersBefore = _scheduler.ListenerManager.GetSchedulerListeners().Count;
        var subscription = _scheduler.ObserveSchedule(GroupMatcher<TriggerKey>.AnyGroup()).Subscribe(_ => { });
        var listenersSubscribed = _scheduler.ListenerManager.GetSchedulerListeners().Count;

        subscription.Dispose();

        Assert.Equal(listenersBefore + 1, listenersSubscribed);
        Assert.Equal(listenersBefore, _scheduler.ListenerManager.GetSchedulerListeners().Count);
    }

    private static ITrigger LaterTrigger(TriggerKey key) =>
        TriggerBuilder.Create().ForJob(WatchedKey).WithIdentity(key).StartAt(DateTimeOffset.UtcNow.AddHours(1)).Build();

    /// <summary>A job that fails when its trigger's data says so.</summary>
    private sealed class TestJob : IJob
    {
        /// <summary>Throws when the trigger's data sets the fail key, and succeeds otherwise.</summary>
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
            context.MergedJobDataMap.TryGetBoolean(FailKey, out var fail) && fail
                ? throw new InvalidOperationException("The test job failed.")
                : ValueTask.CompletedTask;
    }
}
