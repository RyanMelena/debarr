using Debarr.Notifying;
using FluentResults;

namespace Debarr.Components.Pages.Settings;

/// <summary>A webhook notifier as the webhook modal edits it, with its headers as rows.</summary>
public sealed record WebhookNotifierForm : INotifierForm
{
    /// <summary>The notifier's id, which a new notifier's form generates.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>True for a notifier that is not saved yet.</summary>
    public bool IsNew { get; set; } = true;

    public string Name { get; set; } = "";

    public bool Enabled { get; set; }

    public string Url { get; set; } = "";

    public WebhookMethod Method { get; set; }

    public List<WebhookHeaderForm> Headers { get; set; } = [];

    public static WebhookNotifierForm ForNewNotifier() => new() { Enabled = true, Method = WebhookSettings.DefaultMethod };

    public static WebhookNotifierForm FromWebhookNotifier(Notifier notifier, WebhookSettings settings) => new()
    {
        Id = notifier.Id,
        IsNew = false,
        Name = notifier.Name,
        Enabled = notifier.Enabled,
        Url = settings.Url.OriginalString,
        Method = settings.Method,
        Headers = [.. settings.Headers.Select(header => new WebhookHeaderForm { Name = header.Name, Value = header.Value })],
    };

    /// <summary>A copy whose headers edit apart from this form's.</summary>
    public WebhookNotifierForm Copy() => this with { Headers = [.. Headers.Select(header => header.Copy())] };

    /// <summary>Whether a value or a header differs from <paramref name="saved"/>.</summary>
    public bool HasChangesFrom(WebhookNotifierForm saved) =>
        this with { Headers = saved.Headers } != saved || !Headers.Select(NameAndValue).SequenceEqual(saved.Headers.Select(NameAndValue));

    private static (string Name, string Value) NameAndValue(WebhookHeaderForm header) => (header.Name, header.Value);

    /// <summary>The save of the trimmed values, or a field error for each setting outside its bounds together with the name's field error.</summary>
    public Result<SaveNotifier> ToSaveNotifier(Notifiers notifiers)
    {
        var name = Name.Trim();
        var settings = WebhookSettings.Create(Url.Trim(), Method, [.. Headers.Select(header => new WebhookHeader(header.Name.Trim(), header.Value.Trim()))]);
        return settings.IsSuccess
            ? new SaveNotifier(Id, name, Enabled, settings.Value)
            : Result.Fail([.. SaveNotifierHandler.ValidateName(Id, name, notifiers), .. settings.Errors]);
    }
}
