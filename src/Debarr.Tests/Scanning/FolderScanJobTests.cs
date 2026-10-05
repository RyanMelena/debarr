using Debarr.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Quartz;

namespace Debarr.Tests.Scanning;

public sealed class FolderScanJobTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Movies = Path.Combine(Path.GetTempPath(), "movies");

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
    public async Task A_folder_scan_fires_after_its_delay_in_the_scan_execution_group()
    {
        await FolderScanJob.ScheduleAsync(_scheduler, Movies, TimeSpan.FromSeconds(10), CancellationToken);

        var trigger = Assert.IsAssignableFrom<ISimpleTrigger>(await _scheduler.GetTrigger(new TriggerKey(Movies, "folder-scan"), CancellationToken));
        Assert.Equal(FolderScanJob.Key, trigger.JobKey);
        Assert.Equal(Start.AddSeconds(10), trigger.NextFireTimeUtc);
        Assert.Equal(LibraryScanner.ExecutionGroup, trigger.ExecutionGroup);
        Assert.Equal(SimpleTriggerMisfireInstruction.FireNow, trigger.MisfireInstruction);
    }

    [Fact]
    public async Task Scheduling_a_folder_scan_again_pushes_it_back()
    {
        await FolderScanJob.ScheduleAsync(_scheduler, Movies, TimeSpan.FromSeconds(10), CancellationToken);

        _timeProvider.Advance(TimeSpan.FromSeconds(4));
        await FolderScanJob.ScheduleAsync(_scheduler, Movies, TimeSpan.FromSeconds(10), CancellationToken);

        var trigger = Assert.Single(await _scheduler.GetTriggersOfJob(FolderScanJob.Key, CancellationToken));
        Assert.Equal(Start.AddSeconds(14), trigger.NextFireTimeUtc);
    }

    [Fact]
    public async Task Unscheduling_a_root_removes_the_waiting_scans_of_the_root_and_its_folders_only()
    {
        var subfolder = Path.Combine(Movies, "sub");
        var sibling = Movies + "2";
        foreach (var folder in new[] { Movies, subfolder, sibling })
        {
            await FolderScanJob.ScheduleAsync(_scheduler, folder, TimeSpan.FromSeconds(10), CancellationToken);
        }

        await FolderScanJob.UnscheduleUnderAsync(_scheduler, new DirectoryInfo(Movies), CancellationToken);

        var remaining = await _scheduler.GetTriggersOfJob(FolderScanJob.Key, CancellationToken);
        Assert.Equal([sibling], remaining.Select(trigger => trigger.Key.Name));
    }
}
