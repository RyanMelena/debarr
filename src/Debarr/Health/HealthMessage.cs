namespace Debarr.Health;

/// <summary>One problem a health check found, and the page that fixes it.</summary>
/// <param name="Text">What is wrong, as a sentence.</param>
/// <param name="Href">The page that fixes it, relative to the base URL.</param>
/// <param name="LinkText">The link's text, such as Fix in Settings &gt; Library.</param>
/// <param name="Since">When the problem began, or when it was last seen; null when unknown.</param>
public sealed record HealthMessage(HealthSeverity Severity, string Text, string Href, string LinkText, DateTimeOffset? Since = null);
