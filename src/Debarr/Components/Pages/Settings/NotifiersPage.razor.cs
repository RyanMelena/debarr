using Debarr.Extensions;
using Debarr.Notifying;
using Debarr.Playing;
using Fisher;
using MudBlazor;

namespace Debarr.Components.Pages.Settings;

public partial class NotifiersPage(IDocumentStore store, IDialogService dialogService)
{
    private static readonly DialogOptions ModalOptions = new() { MaxWidth = MaxWidth.Small, FullWidth = true, BackdropClick = false };

    private static readonly DialogOptions TypeModalOptions = new() { MaxWidth = MaxWidth.Small, FullWidth = true };

    private List<Notifier>? _notifiers;
    private Dictionary<Guid, Delivery> _lastDeliveries = [];

    protected override IReadOnlySet<string> ReadModels { get; } = new HashSet<string>
    {
        nameof(Notifiers),
        NotifierDeliveryProjection.ReadModel,
        HistoryClearProjection.ReadModel,
    };

    protected override async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        _notifiers = [.. (await Notifiers.ReadAsync(session, cancellationToken)).All.OrderBy(notifier => notifier.Name, StringComparer.Ordinal)];
        _lastDeliveries = [];
        foreach (var notifier in _notifiers)
        {
            if ((await session.ReadNewestDeliveriesAsync(notifier.Id, 1, cancellationToken)).FirstOrDefault() is { } delivery)
            {
                _lastDeliveries[notifier.Id] = delivery;
            }
        }
    }

    private static string HeadersText(WebhookSettings webhook) => webhook.Headers.Count switch
    {
        0 => "No headers",
        1 => "1 header",
        var count => $"{count} headers",
    };

    /// <summary>Asks for the notifier type, then opens that type's modal with a new notifier.</summary>
    private async Task AddAsync()
    {
        var dialog = await dialogService.ShowAsync<NotifierTypeModal>("Add Notifier", TypeModalOptions);
        var result = await dialog.Result;
        switch (result?.Data)
        {
            case MqttNotifierForm form:
                await ShowMqttAsync("Add Notifier - MQTT", form);
                break;
            case WebhookNotifierForm form:
                await ShowWebhookAsync("Add Notifier - Webhook", form);
                break;
        }
    }

    private Task EditAsync(Notifier notifier) => notifier.Settings switch
    {
        MqttSettings mqtt => ShowMqttAsync("Edit Notifier - MQTT", MqttNotifierForm.FromMqttNotifier(notifier, mqtt)),
        WebhookSettings webhook => ShowWebhookAsync("Edit Notifier - Webhook", WebhookNotifierForm.FromWebhookNotifier(notifier, webhook)),
        _ => Task.CompletedTask,
    };

    private Task ShowMqttAsync(string title, MqttNotifierForm form) =>
        dialogService.ShowAsync<MqttNotifierModal>(
            title,
            new DialogParameters<MqttNotifierModal> { { modal => modal.Saved, form } },
            ModalOptions);

    private Task ShowWebhookAsync(string title, WebhookNotifierForm form) =>
        dialogService.ShowAsync<WebhookNotifierModal>(
            title,
            new DialogParameters<WebhookNotifierModal> { { modal => modal.Saved, form } },
            ModalOptions);
}
