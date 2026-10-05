using Debarr.Components.Pages.Settings;
using Debarr.Notifying;

namespace Debarr.Tests.Components.Pages.Settings;

public class WebhookNotifierFormTests
{
    [Fact]
    public void Headers_round_trip_through_a_json_object_of_name_to_value()
    {
        var form = new WebhookNotifierForm
        {
            Name = " automation ",
            Url = " http://automation.lan/debarr ",
            Method = WebhookMethod.Put,
            Headers =
            [
                new WebhookHeaderForm { Name = " Authorization ", Value = " Bearer abc " },
                new WebhookHeaderForm { Name = "X-Debarr", Value = "1" },
            ],
        };

        var (notifier, settings) = Store(form.ToSaveNotifier(Notifiers.Empty).Value);
        var reloaded = WebhookNotifierForm.FromWebhookNotifier(notifier, settings);

        Assert.Equal(("automation", "http://automation.lan/debarr", WebhookMethod.Put), (notifier.Name, settings.Url.OriginalString, settings.Method));
        Assert.Equal([new WebhookHeader("Authorization", "Bearer abc"), new WebhookHeader("X-Debarr", "1")], settings.Headers);
        Assert.Equal(("http://automation.lan/debarr", WebhookMethod.Put), (reloaded.Url, reloaded.Method));
        Assert.Equal([("Authorization", "Bearer abc"), ("X-Debarr", "1")], reloaded.Headers.Select(header => (header.Name, header.Value)));
    }

    [Fact]
    public void No_headers_are_saved_as_none_and_shown_as_no_rows()
    {
        var (notifier, settings) = Store(new WebhookNotifierForm { Name = "automation", Url = "http://automation.lan/debarr" }.ToSaveNotifier(Notifiers.Empty).Value);

        Assert.Empty(settings.Headers);
        Assert.Empty(WebhookNotifierForm.FromWebhookNotifier(notifier, settings).Headers);
    }

    [Fact]
    public void A_new_webhook_saves_with_an_id_of_its_own_and_a_saved_one_keeps_its_id()
    {
        var savedSettings = WebhookSettings.Create("http://automation.lan/debarr", WebhookMethod.Post, []).Value;
        var saved = new Notifier(Guid.NewGuid(), "automation", true, savedSettings);

        var added = WebhookNotifierForm.ForNewNotifier();
        added.Url = "http://automation.lan/debarr";
        var edited = WebhookNotifierForm.FromWebhookNotifier(saved, savedSettings);

        Assert.Equal((true, false), (added.IsNew, added.ToSaveNotifier(Notifiers.Empty).Value.NotifierId == Guid.Empty));
        Assert.Equal((false, saved.Id), (edited.IsNew, edited.ToSaveNotifier(Notifiers.Empty).Value.NotifierId));
    }

    [Fact]
    public void A_new_webhook_is_enabled_and_takes_the_default_method()
    {
        var form = WebhookNotifierForm.ForNewNotifier();

        Assert.Equal((true, WebhookMethod.Post), (form.Enabled, form.Method));
    }

    [Fact]
    public void A_save_refuses_a_blank_name_a_url_and_header_names_outside_their_bounds_together_beneath_their_fields()
    {
        var form = new WebhookNotifierForm
        {
            Name = " ",
            Url = "ftp://automation.lan/debarr",
            Headers =
            [
                new WebhookHeaderForm { Name = "Authorization", Value = "Bearer abc" },
                new WebhookHeaderForm { Name = " ", Value = "1" },
                new WebhookHeaderForm { Name = "authorization", Value = "Bearer def" },
            ],
        };

        Assert.Equal(
            [
                (nameof(WebhookNotifierForm.Name), "Enter a name."),
                (nameof(WebhookNotifierForm.Url), "Enter an absolute http or https URL."),
                ("Headers[0].Name", "Authorization is set twice."),
                ("Headers[1].Name", "Enter the header name."),
                ("Headers[2].Name", "authorization is set twice."),
            ],
            form.ToSaveNotifier(Notifiers.Empty).Errors.Cast<FieldError>().Select(error => (error.Field, error.Message)));
    }

    [Fact]
    public void A_copy_edits_its_headers_apart_and_an_edited_header_is_a_change()
    {
        var saved = new WebhookNotifierForm { Headers = [new WebhookHeaderForm { Name = "Authorization", Value = "Bearer a" }] };
        var edited = saved.Copy();

        edited.Headers[0].Value = "Bearer b";

        Assert.Equal("Bearer a", saved.Headers[0].Value);
        Assert.True(edited.HasChangesFrom(saved));
        Assert.False(saved.Copy().HasChangesFrom(saved));
    }

    private static (Notifier Notifier, WebhookSettings Settings) Store(SaveNotifier command) =>
        (new Notifier(command.NotifierId, command.Name, command.Enabled, command.Settings), (WebhookSettings)command.Settings);
}
