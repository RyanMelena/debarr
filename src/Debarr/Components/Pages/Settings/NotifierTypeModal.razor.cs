using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Debarr.Components.Pages.Settings;

/// <summary>Offers a tile for each notifier type, and closes with the form of a new notifier of the type chosen, with that type's defaults.</summary>
public partial class NotifierTypeModal
{
    private static readonly NotifierType[] NotifierTypes =
    [
        new(
            "notifiers-add-mqtt",
            "MQTT",
            "Publishes the ratio to a topic on an MQTT broker when a player starts playback, for Home Assistant or any other subscriber.",
            MqttNotifierForm.ForNewNotifier),
        new(
            "notifiers-add-webhook",
            "Webhook",
            "Sends the ratio as JSON to a URL when a player starts playback.",
            WebhookNotifierForm.ForNewNotifier),
    ];

    [CascadingParameter]
    private IMudDialogInstance Dialog { get; set; } = default!;

    private void Choose(NotifierType notifierType) => Dialog.Close(DialogResult.Ok(notifierType.NewForm()));

    private void Cancel() => Dialog.Cancel();

    /// <summary>One notifier type as its tile shows it.</summary>
    private sealed record NotifierType(string Id, string Name, string Description, Func<object> NewForm);
}
