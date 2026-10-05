namespace Debarr.EventStore;

/// <summary>A stream and the version a read model folded it at, which an append decided from the read model states.</summary>
public sealed record StreamVersion(Guid Id, long Version);
