using Quartz;

namespace Debarr.Scanning;

/// <summary>A start or end of a Quartz.NET job, whose context is read while the job listener's call lasts.</summary>
public abstract record JobEvent(IJobExecutionContext Context);
