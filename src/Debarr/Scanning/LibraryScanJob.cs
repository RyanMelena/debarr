using Quartz;

namespace Debarr.Scanning;

/// <summary>The Quartz job that runs a library scan, on its recurring schedule or at once for startup, Scan now and a watcher error.</summary>
public sealed class LibraryScanJob(LibraryScanner scanner) : IJob
{
    public static readonly JobKey Key = new("full", LibraryScanner.ExecutionGroup);

    private const string TriggerGroup = "library-scan";

    /// <summary>The trigger of the recurring schedule.</summary>
    public static readonly TriggerKey ScheduledTriggerKey = new("scheduled", TriggerGroup);

    private static readonly TriggerKey ImmediateTriggerKey = new("immediate", TriggerGroup);

    /// <summary>
    /// Scans every enabled root folder, then archives the video files whose last file path left before the scan.
    /// A scan a scan pause cancelled runs again once the pause ends.
    /// </summary>
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var outcome = await scanner.LibraryScanAsync(cancellationToken);
        context.Result = outcome;
        if (outcome == ScanOutcome.Cancelled)
        {
            await TriggerNowAsync(context.Scheduler, cancellationToken);
        }
    }

    /// <summary>
    /// Schedules a library scan every interval, the first one interval from now, in place of the earlier schedule.
    /// An interval that is off removes the schedule.
    /// </summary>
    public static async Task ScheduleAsync(IScheduler scheduler, ScanInterval scanInterval, CancellationToken cancellationToken)
    {
        if (scanInterval.Hours is not { } hours)
        {
            await scheduler.UnscheduleJob(ScheduledTriggerKey, cancellationToken);
            return;
        }

        var interval = TimeSpan.FromHours(hours);
        var trigger = TriggerBuilder.Create()
            .ForJob(Key)
            .WithIdentity(ScheduledTriggerKey)
            .WithExecutionGroup(LibraryScanner.ExecutionGroup)
            .StartAt(scheduler.TimeProvider.GetUtcNow() + interval)
            .WithSimpleSchedule(schedule => schedule
                .WithInterval(interval)
                .RepeatForever()
                .WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow))
            .Build();

        await scheduler.ScheduleJob(trigger, ScheduleJobOptions.Replacing, cancellationToken);
    }

    /// <summary>When the scheduled library scan runs next; null while the schedule is off.</summary>
    public static async Task<DateTimeOffset?> ReadNextScheduledRunAsync(IScheduler scheduler, CancellationToken cancellationToken) =>
        (await scheduler.GetTrigger(ScheduledTriggerKey, cancellationToken))?.NextFireTimeUtc;

    /// <summary>Runs a library scan now, or once the scan in progress ends, unless one is already waiting to run.</summary>
    public static async Task TriggerNowAsync(IScheduler scheduler, CancellationToken cancellationToken)
    {
        if (await scheduler.GetTriggerState(ImmediateTriggerKey, cancellationToken) == TriggerState.Normal)
        {
            return;
        }

        // A trigger whose scan is running stays stored, and its replacement fires after that scan ends.
        var trigger = TriggerBuilder.Create()
            .ForJob(Key)
            .WithIdentity(ImmediateTriggerKey)
            .WithExecutionGroup(LibraryScanner.ExecutionGroup)
            .StartNow()
            .WithSimpleSchedule(schedule => schedule.WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow))
            .Build();

        await scheduler.ScheduleJob(trigger, ScheduleJobOptions.Replacing, cancellationToken);
    }
}
