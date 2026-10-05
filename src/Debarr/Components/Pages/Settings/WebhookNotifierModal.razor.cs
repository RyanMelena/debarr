using Debarr.Notifying;

namespace Debarr.Components.Pages.Settings;

public partial class WebhookNotifierModal() : NotifierModalBase<WebhookNotifierForm>(form => form.Copy(), (edited, saved) => edited.HasChangesFrom(saved))
{
    private readonly HashSet<WebhookHeaderForm> _shownHeaders = [];

    protected override WebhookNotifierForm? ToForm(Notifier notifier) =>
        notifier.Settings is WebhookSettings webhook ? WebhookNotifierForm.FromWebhookNotifier(notifier, webhook) : null;

    private void AddHeader() => Form.Headers.Add(new WebhookHeaderForm());

    private void RemoveHeader(WebhookHeaderForm header)
    {
        Form.Headers.Remove(header);
        _shownHeaders.Remove(header);
        ModalAction.ClearRowFieldErrors(nameof(WebhookSettings.Headers));
    }

    private string HeaderNameField(WebhookHeaderForm header) => WebhookSettings.HeaderNameField(Form.Headers.IndexOf(header));

    private void ToggleHeaderShown(WebhookHeaderForm header)
    {
        if (!_shownHeaders.Remove(header))
        {
            _shownHeaders.Add(header);
        }
    }
}
