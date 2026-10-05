namespace Debarr.Activity;

/// <summary>A commit changed a read model for the streams it names.</summary>
/// <param name="ReadModel">The name of the projection that writes the read model, or of the aggregate a page folds.</param>
/// <param name="Streams">The key of each stream whose events the commit projected, or its id as a string.</param>
public sealed record ReadModelChanged(string ReadModel, IReadOnlySet<string> Streams) : ActivityEvent;
