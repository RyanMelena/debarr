using System.Text.Json;
using Debarr.Notifying;

namespace Debarr.Tests.Notifying;

public sealed class SaveNotifierTests
{
    private static readonly Guid BrokerId = Guid.NewGuid();

    private static readonly Guid LightsId = Guid.NewGuid();

    private static readonly MqttSettings Broker = MqttSettings.Create("mqtt://broker.lan", null, null, null, "debarr/player/{player}/playback", QualityOfService.AtLeastOnce).Value;

    /// <summary>broker and lights.</summary>
    private static readonly Notifiers Stored = Notifiers.Create(new NotifierAdded(BrokerId, "broker", true, Broker))
        .Apply(new NotifierAdded(LightsId, "lights", true, MqttSettings.Create("mqtt://broker.lan", null, null, null, "lights/{player}", QualityOfService.AtLeastOnce).Value));

    [Fact]
    public void A_new_notifier_is_added()
    {
        var command = new SaveNotifier(BrokerId, "broker", true, Broker);

        Assert.True(SaveNotifierHandler.Validate(command, null).IsSuccess);
        Assert.Equal([new NotifierAdded(BrokerId, "broker", true, Broker)], SaveNotifierHandler.Handle(command, null));
    }

    [Fact]
    public void A_save_under_the_same_name_changes_the_settings()
    {
        var settings = MqttSettings.Create("mqtts://broker.lan", null, null, null, "debarr/player/{player}/playback", QualityOfService.ExactlyOnce).Value;
        var command = new SaveNotifier(BrokerId, "broker", false, settings);

        Assert.True(SaveNotifierHandler.Validate(command, Stored).IsSuccess);
        Assert.Equal([new NotifierChanged(BrokerId, false, settings)], SaveNotifierHandler.Handle(command, Stored));
    }

    [Fact]
    public void A_save_under_a_new_name_renames_the_notifier_and_changes_its_settings()
    {
        var command = new SaveNotifier(BrokerId, "automation", true, Broker);

        Assert.True(SaveNotifierHandler.Validate(command, Stored).IsSuccess);
        Assert.Equal([new NotifierRenamed(BrokerId, "automation"), new NotifierChanged(BrokerId, true, Broker)], SaveNotifierHandler.Handle(command, Stored));
    }

    [Fact]
    public void A_name_another_notifier_has_is_refused_beneath_its_field()
    {
        var added = new SaveNotifier(Guid.NewGuid(), "broker", true, Broker);
        var renamed = new SaveNotifier(BrokerId, "lights", true, Broker);

        foreach (var command in new[] { added, renamed })
        {
            var error = Assert.IsType<FieldError>(Assert.Single(SaveNotifierHandler.Validate(command, Stored).Errors));
            Assert.Equal((nameof(SaveNotifier.Name), $"A notifier named {command.Name} already exists."), (error.Field, error.Message));
        }
    }

    [Fact]
    public void A_rename_frees_the_old_name_for_another_notifier()
    {
        var notifiers = Stored.Apply(new NotifierRenamed(BrokerId, "automation"));

        Assert.True(SaveNotifierHandler.Validate(new SaveNotifier(LightsId, "broker", true, Broker), notifiers).IsSuccess);
    }

    [Fact]
    public void A_removal_frees_the_name()
    {
        var notifiers = Stored.Apply(new NotifierRemoved(BrokerId));

        Assert.True(SaveNotifierHandler.Validate(new SaveNotifier(Guid.NewGuid(), "broker", true, Broker), notifiers).IsSuccess);
    }

    [Fact]
    public void A_name_that_differs_from_another_notifier_only_in_case_is_accepted()
    {
        var command = new SaveNotifier(Guid.NewGuid(), "Broker", true, Broker);

        Assert.True(SaveNotifierHandler.Validate(command, Stored).IsSuccess);
    }

    [Fact]
    public void A_save_of_a_removed_notifier_is_refused()
    {
        var command = new SaveNotifier(BrokerId, "broker", true, Broker);

        Assert.Equal(
            "The notifier broker was removed.",
            Assert.Single(SaveNotifierHandler.Validate(command, Stored.Apply(new NotifierRemoved(BrokerId))).Errors).Message);
    }

    [Fact]
    public void A_save_that_changes_the_type_is_refused()
    {
        var command = new SaveNotifier(BrokerId, "broker", true, WebhookSettings.Create("http://automation.lan/debarr", WebhookMethod.Post, []).Value);

        Assert.Equal("The notifier broker keeps its type.", Assert.Single(SaveNotifierHandler.Validate(command, Stored).Errors).Message);
    }

    [Fact]
    public void A_blank_name_and_settings_read_with_qos_3_are_refused_beneath_their_fields()
    {
        var settings = JsonSerializer.Deserialize<NotifierSettings>(
            """{"$type":"mqtt","broker":{"host":"broker.lan","port":1883,"tls":false},"topicTemplate":"debarr/{player}","qos":3}""",
            JsonSerializerOptions.Web)!;
        var command = new SaveNotifier(BrokerId, " ", true, settings);

        var errors = SaveNotifierHandler.Validate(command, null).Errors.Cast<FieldError>().Select(error => (error.Field, error.Message));

        Assert.Equal(
            [
                (nameof(SaveNotifier.Name), "Enter a name."),
                (nameof(MqttSettings.Qos), "Enter 0, 1 or 2."),
            ],
            errors);
    }

    [Fact]
    public void The_notifiers_follow_their_events()
    {
        var webhook = WebhookSettings.Create("http://automation.lan/debarr", WebhookMethod.Put, [new WebhookHeader("Authorization", "Bearer abc")]).Value;
        var automationId = Guid.NewGuid();

        var notifiers = Stored
            .Apply(new NotifierAdded(automationId, "automation", true, webhook))
            .Apply(new NotifierRenamed(automationId, "hooks"))
            .Apply(new NotifierChanged(automationId, false, WebhookSettings.Create("http://automation.lan/debarr", WebhookMethod.Patch, []).Value))
            .Apply(new NotifierRemoved(BrokerId));

        Assert.Equal([LightsId, automationId], notifiers.All.Select(notifier => notifier.Id));
        var notifier = notifiers.Find(automationId)!;
        Assert.Equal(("hooks", false, WebhookMethod.Patch), (notifier.Name, notifier.Enabled, ((WebhookSettings)notifier.Settings).Method));
        Assert.Equal([BrokerId], notifiers.RemovedIds);
    }
}
