namespace Debarr.Notifying;

public sealed record NotifierAdded(Guid NotifierId, string Name, bool Enabled, NotifierSettings Settings);

public sealed record NotifierChanged(Guid NotifierId, bool Enabled, NotifierSettings Settings);

public sealed record NotifierRenamed(Guid NotifierId, string Name);

public sealed record NotifierRemoved(Guid NotifierId);
