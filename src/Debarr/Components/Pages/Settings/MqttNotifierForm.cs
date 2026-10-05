using Debarr.Notifying;
using FluentResults;

namespace Debarr.Components.Pages.Settings;

/// <summary>An MQTT notifier as the MQTT modal edits it, with an empty string for each unset optional value.</summary>
/// <remarks>The password is kept as typed, spaces included.</remarks>
public sealed record MqttNotifierForm : INotifierForm
{
    /// <summary>The notifier's id, which a new notifier's form generates.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>True for a notifier that is not saved yet.</summary>
    public bool IsNew { get; set; } = true;

    public string Name { get; set; } = "";

    public bool Enabled { get; set; }

    public string Url { get; set; } = "";

    public string ClientId { get; set; } = "";

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    public string TopicTemplate { get; set; } = "";

    public int Qos { get; set; }

    public static MqttNotifierForm ForNewNotifier() => new()
    {
        Enabled = true,
        TopicTemplate = MqttSettings.DefaultTopicTemplate,
        Qos = (int)MqttSettings.DefaultQos,
    };

    public static MqttNotifierForm FromMqttNotifier(Notifier notifier, MqttSettings settings) => new()
    {
        Id = notifier.Id,
        IsNew = false,
        Name = notifier.Name,
        Enabled = notifier.Enabled,
        Url = settings.Broker.ToUrl(),
        ClientId = settings.ClientId ?? "",
        Username = settings.Username ?? "",
        Password = settings.Password ?? "",
        TopicTemplate = settings.TopicTemplate,
        Qos = (int)settings.Qos,
    };

    /// <summary>The save of the trimmed values, or a field error for each setting outside its bounds together with the name's field error.</summary>
    public Result<SaveNotifier> ToSaveNotifier(Notifiers notifiers)
    {
        var name = Name.Trim();
        var settings = MqttSettings.Create(
            Url.Trim(),
            string.IsNullOrWhiteSpace(ClientId) ? null : ClientId.Trim(),
            string.IsNullOrWhiteSpace(Username) ? null : Username.Trim(),
            Password.Length > 0 ? Password : null,
            TopicTemplate.Trim(),
            (QualityOfService)Qos);
        return settings.IsSuccess
            ? new SaveNotifier(Id, name, Enabled, settings.Value)
            : Result.Fail([.. SaveNotifierHandler.ValidateName(Id, name, notifiers), .. settings.Errors]);
    }
}
