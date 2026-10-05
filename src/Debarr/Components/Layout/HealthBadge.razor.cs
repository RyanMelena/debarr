using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Debarr.Activity;
using Debarr.Extensions;
using Debarr.Health;

namespace Debarr.Components.Layout;

/// <summary>The number of health messages, red when any is an error and amber otherwise, hidden when there are none.</summary>
public partial class HealthBadge(HealthCheckService healthCheckService)
{
    private IReadOnlyList<HealthMessage> _messages = [];

    private bool HasError => _messages.Any(message => message.Severity == HealthSeverity.Error);

    /// <summary>Such as "1 error, 2 warnings".</summary>
    private string Description => string.Join(
        ", ",
        new[]
        {
            (Count: _messages.Count(message => message.Severity == HealthSeverity.Error), Singular: "error", Plural: "errors"),
            (Count: _messages.Count(message => message.Severity == HealthSeverity.Warning), Singular: "warning", Plural: "warnings"),
        }
        .Where(severity => severity.Count > 0)
        .Select(severity => severity.Count.ToCountText(severity.Singular, severity.Plural)));

    protected override IReadOnlySet<string> ReadModels { get; } = new HashSet<string>();

    protected override bool ShowsActivity(ActivityEvent activityEvent) => activityEvent is HealthChangedEvent;

    protected override async Task ReloadAsync(CancellationToken cancellationToken) =>
        _messages = await healthCheckService.Messages.FirstAsync().ToTask(cancellationToken);
}
