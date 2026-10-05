using Quartz;

namespace Debarr.Scanning;

public sealed record JobStartedEvent(IJobExecutionContext Context) : JobEvent(Context);
