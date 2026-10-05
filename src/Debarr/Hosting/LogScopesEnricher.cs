using Serilog.Core;
using Serilog.Events;

namespace Debarr.Hosting;

/// <summary>
/// Adds a Scopes property that renders the open scopes, outermost first, as " (first, second)" after a message.
/// The property is absent when no scope is open.
/// </summary>
public sealed class LogScopesEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent.Properties.TryGetValue("Scope", out var scope) && scope is SequenceValue { Elements.Count: > 0 } scopes)
        {
            var texts = scopes.Elements.Select(element => element is ScalarValue { Value: string text } ? text : element.ToString());
            logEvent.AddOrUpdateProperty(new LogEventProperty("Scopes", new ScalarValue($" ({string.Join(", ", texts)})")));
        }
    }
}
