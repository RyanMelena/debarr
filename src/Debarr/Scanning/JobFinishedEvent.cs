using Quartz;

namespace Debarr.Scanning;

/// <summary>A job's end, with the exception it threw wrapped by Quartz.NET, or null when it succeeded.</summary>
public sealed record JobFinishedEvent(IJobExecutionContext Context, JobExecutionException? Exception) : JobEvent(Context);
