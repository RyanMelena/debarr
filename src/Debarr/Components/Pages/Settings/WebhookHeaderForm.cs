namespace Debarr.Components.Pages.Settings;

/// <summary>One webhook header as the webhook modal edits it.</summary>
public sealed class WebhookHeaderForm
{
    public string Name { get; set; } = "";

    public string Value { get; set; } = "";

    public WebhookHeaderForm Copy() => new() { Name = Name, Value = Value };
}
