using Debarr.Components.Pages.Settings;
using Debarr.Notifying;

namespace Debarr.Tests.Components.Pages.Settings;

public class MqttNotifierFormTests
{
    private static readonly Guid NotifierId = Guid.NewGuid();

    [Fact]
    public void To_save_notifier_trims_the_values_and_turns_empty_optional_values_into_null()
    {
        var form = new MqttNotifierForm
        {
            Id = NotifierId,
            Name = " broker ",
            Enabled = true,
            Url = " mqtt://broker.lan ",
            ClientId = "",
            Username = "  ",
            Password = "",
            TopicTemplate = " debarr/{player} ",
            Qos = 1,
        };

        var command = form.ToSaveNotifier(Notifiers.Empty).Value;
        var settings = Assert.IsType<MqttSettings>(command.Settings);

        Assert.Equal(
            (NotifierId, "broker", true, new MqttBrokerAddress("broker.lan", 1883, false), (string?)null, (string?)null, (string?)null, "debarr/{player}", QualityOfService.AtLeastOnce),
            (command.NotifierId, command.Name, command.Enabled, settings.Broker, settings.ClientId, settings.Username, settings.Password, settings.TopicTemplate, settings.Qos));
    }

    [Fact]
    public void To_save_notifier_keeps_the_password_as_typed()
    {
        var form = new MqttNotifierForm { Name = "broker", Url = "mqtt://broker.lan", Password = " secret ", TopicTemplate = "debarr/{player}" };

        Assert.Equal(" secret ", Assert.IsType<MqttSettings>(form.ToSaveNotifier(Notifiers.Empty).Value.Settings).Password);
    }

    [Fact]
    public void A_new_notifier_takes_the_mqtt_defaults_and_an_id_of_its_own()
    {
        var form = MqttNotifierForm.ForNewNotifier();

        Assert.Equal(
            (true, true, "", "", "", "", "debarr/player/{player}/playback", 1),
            (form.IsNew, form.Enabled, form.Url, form.ClientId, form.Username, form.Password, form.TopicTemplate, form.Qos));
        Assert.NotEqual(Guid.Empty, form.Id);
    }

    [Fact]
    public void From_mqtt_notifier_shows_the_broker_as_a_url_and_each_unset_optional_value_as_an_empty_string()
    {
        var settings = MqttSettings.Create("mqtts://broker.lan", null, null, null, "debarr/{player}", QualityOfService.ExactlyOnce).Value;

        var form = MqttNotifierForm.FromMqttNotifier(new Notifier(NotifierId, "broker", true, settings), settings);

        Assert.Equal(
            (NotifierId, false, "mqtts://broker.lan:8883", "", "", "", "debarr/{player}", 2),
            (form.Id, form.IsNew, form.Url, form.ClientId, form.Username, form.Password, form.TopicTemplate, form.Qos));
    }

    [Fact]
    public void A_save_refuses_each_setting_outside_its_bounds_beneath_its_field()
    {
        var form = new MqttNotifierForm { Name = "broker", Url = "http://broker.lan", TopicTemplate = " ", Qos = 3 };

        Assert.Equal(
            [
                (nameof(MqttNotifierForm.Url), "Enter an mqtt or mqtts URL of a host and an optional port."),
                (nameof(MqttNotifierForm.TopicTemplate), "Enter the topic template."),
                (nameof(MqttNotifierForm.Qos), "Enter 0, 1 or 2."),
            ],
            form.ToSaveNotifier(Notifiers.Empty).Errors.Cast<FieldError>().Select(error => (error.Field, error.Message)));
    }
}
