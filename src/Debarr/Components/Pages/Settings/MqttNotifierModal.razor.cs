using Debarr.Notifying;

namespace Debarr.Components.Pages.Settings;

public partial class MqttNotifierModal() : NotifierModalBase<MqttNotifierForm>(form => form with { })
{
    private bool _passwordShown;

    public override IReadOnlyCollection<string> AdvancedFields { get; } = [nameof(MqttNotifierForm.ClientId), nameof(MqttNotifierForm.Qos)];

    protected override MqttNotifierForm? ToForm(Notifier notifier) =>
        notifier.Settings is MqttSettings mqtt ? MqttNotifierForm.FromMqttNotifier(notifier, mqtt) : null;

    private void TogglePasswordShown() => _passwordShown = !_passwordShown;
}
