using Debarr.EventStore;
using Debarr.Health;
using Debarr.Notifying;
using Debarr.Playing;
using Debarr.Tests.Playing;
using Fisher;
using Wolverine.Runtime;

namespace Debarr.Tests.Notifying;

public sealed class NotifierHealthCheckTests : AppTestContext
{
    private static readonly DateTimeOffset FirstStartedAt = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_enabled_notifier_whose_last_delivery_failed_is_an_error_since_that_delivery()
    {
        await SeedNotifierAsync("Home Assistant", enabled: true, new DeliveryOutcome.Succeeded(), new DeliveryOutcome.Failed("Connection refused."), new DeliveryOutcome.Cancelled());

        var message = Assert.Single(await CheckAsync<NotifierHealthCheck>());

        Assert.Equal(
            (HealthSeverity.Error, "Home Assistant failed to deliver the last notification: Connection refused.", "settings/notifiers", FirstStartedAt.AddSeconds(1)),
            (message.Severity, message.Text, message.Href, message.Since));
    }

    [Fact]
    public async Task A_notifier_whose_last_delivery_succeeded_finds_nothing()
    {
        await SeedNotifierAsync("Sink", enabled: true, new DeliveryOutcome.Failed("Connection refused."), new DeliveryOutcome.Succeeded());

        Assert.Empty(await CheckAsync<NotifierHealthCheck>());
    }

    [Fact]
    public async Task A_disabled_notifier_finds_nothing()
    {
        await SeedNotifierAsync("Home Assistant", enabled: false, new DeliveryOutcome.Failed("Connection refused."));

        Assert.Empty(await CheckAsync<NotifierHealthCheck>());
    }

    [Fact]
    public async Task The_check_runs_again_when_a_commit_changes_a_notifier_a_delivery_or_the_history()
    {
        var notifier = new SaveNotifier(Guid.NewGuid(), "Home Assistant", true, WebhookSettings.Create("http://localhost:9/", WebhookMethod.Post, []).Value);
        Assert.True((await GetAppService<IWolverineRuntime>().SendCommandAsync(notifier, CancellationToken)).IsSuccess);

        await RecordFailedDeliveryAsync(notifier);
        await WaitForMessagesAsync(HasNotifierError);

        Assert.True((await GetAppService<IWolverineRuntime>().SendCommandAsync(new ClearHistory(), CancellationToken)).IsSuccess);
        await WaitForMessagesAsync(messages => !HasNotifierError(messages));

        await RecordFailedDeliveryAsync(notifier);
        await WaitForMessagesAsync(HasNotifierError);

        Assert.True((await GetAppService<IWolverineRuntime>().SendCommandAsync(notifier with { Enabled = false }, CancellationToken)).IsSuccess);
        await WaitForMessagesAsync(messages => !HasNotifierError(messages));
    }

    private static bool HasNotifierError(IReadOnlyList<HealthMessage> messages) =>
        messages.Any(message => message.Href == "settings/notifiers");

    private Task RecordFailedDeliveryAsync(SaveNotifier notifier) =>
        TestPlayback.RecordAsync(
            GetAppService<IDocumentStore>(),
            TestPlayback.Handled(),
            [TestPlayback.Delivery(notifier.NotifierId, notifier.Name, new DeliveryOutcome.Failed("Connection refused."))],
            CancellationToken);

    /// <summary>Adds a notifier and one playback with a delivery for each outcome, a second apart and oldest first.</summary>
    private async Task SeedNotifierAsync(string name, bool enabled, params DeliveryOutcome[] outcomes)
    {
        var notifier = new SaveNotifier(Guid.NewGuid(), name, enabled, WebhookSettings.Create("http://localhost:9/", WebhookMethod.Post, []).Value);
        Assert.True((await GetAppService<IWolverineRuntime>().SendCommandAsync(notifier, CancellationToken)).IsSuccess);
        await TestPlayback.RecordAsync(
            GetAppService<IDocumentStore>(),
            TestPlayback.Handled(playerPath: "/media/Heat.mkv", occurredAt: FirstStartedAt),
            outcomes.Select((outcome, index) => TestPlayback.Delivery(
                notifier.NotifierId,
                name,
                outcome,
                FirstStartedAt.AddSeconds(index))),
            CancellationToken);
    }
}
