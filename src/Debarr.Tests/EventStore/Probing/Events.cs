namespace Debarr.Tests.EventStore.Probing;

public sealed record ProbeStarted(string Name);

public sealed record ProbeRenamed(string Name);
