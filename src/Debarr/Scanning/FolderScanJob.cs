using Debarr.Extensions;
using Quartz;

namespace Debarr.Scanning;

/// <summary>The Quartz job that runs a folder scan. Each trigger is keyed by its folder, so a folder has at most one scan waiting.</summary>
public sealed class FolderScanJob(LibraryScanner scanner) : IJob
{
    public static readonly JobKey Key = new("folder", LibraryScanner.ExecutionGroup);

    private const string TriggerGroup = "folder-scan";

    /// <summary>Scans the folder that the trigger's key names. A scan a scan pause cancelled runs again once the pause ends.</summary>
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var folder = context.Trigger.Key.Name;
        var outcome = await scanner.ScanFolderAsync(new DirectoryInfo(folder), cancellationToken);
        context.Result = outcome;
        if (outcome == ScanOutcome.Cancelled)
        {
            await ScheduleAsync(context.Scheduler, folder, TimeSpan.Zero, cancellationToken);
        }
    }

    /// <summary>Schedules a folder scan of the folder after the delay, in place of the folder's waiting one.</summary>
    public static async Task ScheduleAsync(IScheduler scheduler, string folder, TimeSpan delay, CancellationToken cancellationToken)
    {
        var trigger = TriggerBuilder.Create()
            .ForJob(Key)
            .WithIdentity(new TriggerKey(folder, TriggerGroup))
            .WithExecutionGroup(LibraryScanner.ExecutionGroup)
            .StartAt(scheduler.TimeProvider.GetUtcNow() + delay)
            .WithSimpleSchedule(schedule => schedule.WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow))
            .Build();

        await scheduler.ScheduleJob(trigger, ScheduleJobOptions.Replacing, cancellationToken);
    }

    /// <summary>Removes the waiting folder scans of the root and of every folder under it.</summary>
    public static async Task UnscheduleUnderAsync(IScheduler scheduler, DirectoryInfo root, CancellationToken cancellationToken)
    {
        var triggerKeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(TriggerGroup), cancellationToken);

        await scheduler.UnscheduleJobs(
            triggerKeys.Where(triggerKey => new DirectoryInfo(triggerKey.Name).IsSameOrUnder(root)).ToList(),
            cancellationToken);
    }
}
