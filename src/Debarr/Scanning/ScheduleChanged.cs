using Debarr.Activity;
using Quartz;

namespace Debarr.Scanning;

/// <summary>A trigger was scheduled or unscheduled, which moves the next run of its job.</summary>
public sealed record ScheduleChanged(TriggerKey Trigger) : ActivityEvent;
