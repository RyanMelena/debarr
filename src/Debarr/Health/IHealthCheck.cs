using System.Reactive;

namespace Debarr.Health;

/// <summary>Looks for one kind of problem, and says when to look again.</summary>
public interface IHealthCheck
{
    HealthCheckKind Kind { get; }

    /// <summary>Fires when what the check reads may have changed. Hot and never terminating.</summary>
    IObservable<Unit> Triggers { get; }

    /// <summary>The problems the check finds now, or none.</summary>
    Task<IReadOnlyList<HealthMessage>> CheckAsync(CancellationToken cancellationToken);
}
