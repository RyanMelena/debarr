using Bunit;
using Debarr.Components.Pages.Settings;
using Debarr.EventStore;
using Debarr.Notifying;
using Debarr.Playing;
using Debarr.Tests.Extensions;
using Debarr.Tests.Playing;
using Fisher;
using Wolverine.Runtime;

namespace Debarr.Tests.Components.Pages.Settings;

public sealed class NotifiersPageTests : PageTestContext
{
    [Fact]
    public void An_empty_page_says_what_a_notifier_is()
    {
        var cut = RenderPage<NotifiersPage>();

        cut.WaitForAssertion(() => Assert.Contains("A notifier carries the ratio to your automation", cut.Find("#notifiers-empty").TextContent), Timeout);
    }

    [Fact]
    public async Task Add_offers_a_tile_for_each_type_and_opens_the_modal_of_the_one_chosen()
    {
        var cut = RenderPage<NotifiersPage>();
        await cut.RaiseClickAsync("#notifiers-add", Timeout);

        cut.WaitForElement("#notifiers-add-webhook", Timeout);
        Assert.Contains("MQTT broker", cut.Find("#notifiers-add-mqtt").TextContent);
        await cut.RaiseClickAsync("#notifiers-add-mqtt", Timeout);

        cut.WaitForElement("#mqtt-url", Timeout);
        Assert.Empty(cut.FindAll("#notifiers-add-mqtt"));
    }

    [Fact]
    public async Task The_mqtt_modal_takes_one_url_that_says_whether_to_use_tls()
    {
        var cut = RenderPage<NotifiersPage>();
        await cut.RaiseClickAsync("#notifiers-add", Timeout);
        await cut.RaiseClickAsync("#notifiers-add-mqtt", Timeout);

        cut.WaitForElement("#mqtt-url", Timeout);
        Assert.Contains("mqtts:// connects over TLS", cut.Markup);
        Assert.Empty(cut.FindAll("#mqtt-tls"));
    }

    [Fact]
    public async Task Test_refuses_a_url_that_is_not_a_broker_beneath_the_url_field_and_clears_once_the_url_changes()
    {
        var cut = RenderPage<NotifiersPage>();
        await cut.RaiseClickAsync("#notifiers-add", Timeout);
        await cut.RaiseClickAsync("#notifiers-add-mqtt", Timeout);
        await cut.RaiseInputAsync("#mqtt-name", "Home Assistant", Timeout);
        await cut.RaiseInputAsync("#mqtt-url", "http://broker.lan", Timeout);

        await cut.RaiseClickAsync("#mqtt-test", Timeout);

        cut.WaitForAssertion(() => Assert.Contains("Enter an mqtt or mqtts URL of a host and an optional port.", Field(cut, "#mqtt-url").TextContent), Timeout);
        Assert.Empty(cut.FindAll("#mqtt-test-result"));
        Assert.Empty(cut.FindAll("#mqtt-error"));

        await cut.RaiseInputAsync("#mqtt-url", "mqtt://broker.lan", Timeout);

        cut.WaitForAssertion(() => Assert.DoesNotContain("Enter an mqtt", Field(cut, "#mqtt-url").TextContent), Timeout);
    }

    [Theory]
    [InlineData("#mqtt-save")]
    [InlineData("#mqtt-test")]
    public async Task A_name_another_notifier_has_is_refused_together_with_a_refused_url(string button)
    {
        var saved = await GetAppService<IWolverineRuntime>().SendCommandAsync(
            new SaveNotifier(Guid.NewGuid(), "Home Assistant", false, MqttSettings.Create("mqtt://broker.lan", null, null, null, "debarr/{player}", QualityOfService.AtLeastOnce).Value),
            CancellationToken);
        Assert.True(saved.IsSuccess);
        var cut = RenderPage<NotifiersPage>();
        await cut.RaiseClickAsync("#notifiers-add", Timeout);
        await cut.RaiseClickAsync("#notifiers-add-mqtt", Timeout);
        await cut.RaiseInputAsync("#mqtt-name", "Home Assistant", Timeout);
        await cut.RaiseInputAsync("#mqtt-url", "http://broker.lan", Timeout);

        await cut.RaiseClickAsync(button, Timeout);

        cut.WaitForAssertion(() => Assert.Contains("Enter an mqtt or mqtts URL of a host and an optional port.", Field(cut, "#mqtt-url").TextContent), Timeout);
        Assert.Contains("A notifier named Home Assistant already exists.", Field(cut, "#mqtt-name").TextContent);
        Assert.Empty(cut.FindAll("#mqtt-error"));
        Assert.Empty(cut.FindAll("#mqtt-test-result"));
    }

    [Fact]
    public async Task Save_refuses_a_blank_name_and_a_url_that_is_not_a_broker_together_beneath_their_fields()
    {
        var cut = RenderPage<NotifiersPage>();
        await cut.RaiseClickAsync("#notifiers-add", Timeout);
        await cut.RaiseClickAsync("#notifiers-add-mqtt", Timeout);
        await cut.RaiseInputAsync("#mqtt-url", "http://broker.lan", Timeout);

        await cut.RaiseClickAsync("#mqtt-save", Timeout);

        cut.WaitForAssertion(() => Assert.Contains("Enter an mqtt or mqtts URL of a host and an optional port.", Field(cut, "#mqtt-url").TextContent), Timeout);
        Assert.Contains("Enter a name.", Field(cut, "#mqtt-name").TextContent);
        Assert.Empty(cut.FindAll("#mqtt-error"));
    }

    [Fact]
    public async Task Save_refuses_a_header_set_twice_beneath_its_row()
    {
        var cut = RenderPage<NotifiersPage>();
        await cut.RaiseClickAsync("#notifiers-add", Timeout);
        await cut.RaiseClickAsync("#notifiers-add-webhook", Timeout);
        await cut.RaiseInputAsync("#webhook-name", "Automation", Timeout);
        await cut.RaiseInputAsync("#webhook-url", "http://automation.lan/debarr", Timeout);
        await cut.RaiseClickAsync("#webhook-add-header", Timeout);
        await cut.RaiseClickAsync("#webhook-add-header", Timeout);
        var names = cut.FindAll(".mud-dialog input").Where(input => input.Closest(".mud-input-control")!.TextContent.Contains("Header")).ToList();
        await names[0].InputAsync("Authorization");
        await names[1].InputAsync("authorization");

        await cut.RaiseClickAsync("#webhook-save", Timeout);

        cut.WaitForAssertion(() => Assert.Contains("authorization is set twice.", cut.Markup), Timeout);
        Assert.Contains("Authorization is set twice.", cut.Markup);
        Assert.Single(cut.FindAll("#webhook-name"));
    }

    [Fact]
    public async Task The_page_reloads_when_a_commit_changes_a_notifier_its_deliveries_or_the_history()
    {
        var cut = RenderPage<NotifiersPage>();
        cut.WaitForElement("#notifiers-empty", Timeout);

        var notifier = new SaveNotifier(Guid.NewGuid(), "Lights", false, WebhookSettings.Create("http://localhost:9/", WebhookMethod.Post, []).Value);
        Assert.True((await GetAppService<IWolverineRuntime>().SendCommandAsync(notifier, CancellationToken)).IsSuccess);
        cut.WaitForAssertion(() => Assert.Contains("No delivery yet", cut.Find(".notifier-card-delivery").TextContent), Timeout);

        await SeedPlaybackAsync(TestPlayback.Handled(), TestPlayback.Delivery(notifier.NotifierId, notifier.Name, new DeliveryOutcome.Succeeded()));
        cut.WaitForAssertion(() => Assert.StartsWith("Last delivery Today ", cut.Find(".notifier-card-delivery").TextContent.Trim()), Timeout);

        await ClearHistoryAsync();
        cut.WaitForAssertion(() => Assert.Contains("No delivery yet", cut.Find(".notifier-card-delivery").TextContent), Timeout);
    }

    [Fact]
    public async Task A_card_shows_its_last_delivery()
    {
        var notifier = new SaveNotifier(Guid.NewGuid(), "Home Assistant", true, MqttSettings.Create("mqtt://broker.lan", null, null, null, "debarr/{player}", QualityOfService.AtLeastOnce).Value);
        Assert.True((await GetAppService<IWolverineRuntime>().SendCommandAsync(notifier, CancellationToken)).IsSuccess);
        await SeedPlaybackAsync(TestPlayback.Handled(), TestPlayback.Delivery(notifier.NotifierId, notifier.Name, new DeliveryOutcome.Failed("Connection refused")));

        var cut = RenderPage<NotifiersPage>();

        cut.WaitForAssertion(() => Assert.StartsWith("Last delivery Today ", cut.Find(".notifier-card-delivery").TextContent.Trim()), Timeout);
        Assert.EndsWith(" · Failed: Connection refused", cut.Find(".notifier-card-delivery").TextContent.Trim());
    }

    [Theory]
    [InlineData("mqtt")]
    [InlineData("webhook")]
    public async Task A_save_from_a_modal_whose_notifier_another_tab_saved_is_refused_and_both_saves_are_kept(string type)
    {
        NotifierSettings settings = type == "mqtt"
            ? MqttSettings.Create("mqtt://broker.lan", null, null, null, "debarr/{player}", QualityOfService.AtLeastOnce).Value
            : WebhookSettings.Create("http://localhost:9/", WebhookMethod.Post, []).Value;
        Assert.True((await GetAppService<IWolverineRuntime>().SendCommandAsync(new SaveNotifier(Guid.NewGuid(), "Lights", false, settings), CancellationToken)).IsSuccess);
        var first = RenderPage<NotifiersPage>();
        var second = RenderPage<NotifiersPage>(tab: OpenAnotherTab());
        await first.RaiseClickAsync(".notifier-card", Timeout);
        await first.RaiseInputAsync($"#{type}-name", "Lounge", Timeout);
        await second.RaiseClickAsync(".notifier-card", Timeout);
        await second.RaiseInputAsync($"#{type}-name", "Bedroom", Timeout);
        await second.RaiseClickAsync($"#{type}-save", Timeout);
        await Poll.UntilAsync(async () => (await ReadNotifiersAsync()).All.Single().Name == "Bedroom", Timeout);

        await first.RaiseClickAsync($"#{type}-save", Timeout);

        first.WaitForAssertion(() => Assert.Equal("Saved in another tab. Reload to see the change.", first.Find($"#{type}-error").TextContent.Trim()), Timeout);
        Assert.Equal("Lounge", first.Find($"#{type}-name").GetAttribute("value"));
        Assert.Equal("Bedroom", (await ReadNotifiersAsync()).All.Single().Name);
    }

    private async Task<Notifiers> ReadNotifiersAsync()
    {
        await using var session = GetAppService<IDocumentStore>().QuerySession();
        return await Notifiers.ReadAsync(session, CancellationToken);
    }

    /// <summary>The control of the field <paramref name="selector"/> finds, which holds its helper and error text.</summary>
    private static AngleSharp.Dom.IElement Field(IRenderedComponent<Debarr.Components.Layout.UISettingsProvider> cut, string selector) =>
        cut.Find(selector).Closest(".mud-input-control")!;
}
