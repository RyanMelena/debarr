using System.Collections.Concurrent;
using System.Net;
using Debarr.EventStore;
using Debarr.Notifying;
using Debarr.Playing;
using Debarr.Tests.Playing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Wolverine.Runtime;

namespace Debarr.Tests.Notifying;

public sealed class NotificationPublisherTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 19, 4, 11, 482, TimeSpan.Zero);

    private readonly FakeTimeProvider _timeProvider = new(Now);
    private readonly CapturingHttpMessageHandler _handler = new();
    private TestHost _host = null!;
    private NotificationPublisher _publisher = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private IEnumerable<string> RequestedUrls => _handler.Requests.Select(request => request.Request.RequestUri!.ToString());

    public async ValueTask InitializeAsync()
    {
        _host = await TestHost.StartAsync(services => services
            .AddSingleton<TimeProvider>(_timeProvider)
            .AddHttpClient(WebhookNotifierClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => _handler));
        _publisher = _host.Services.GetRequiredService<NotificationPublisher>();
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Sending_reaches_every_enabled_notifier_and_skips_a_disabled_one()
    {
        var automationId = await AddWebhookAsync("automation", "http://automation.lan/debarr");
        var lightsId = await AddWebhookAsync("lights", "http://lights.lan/debarr");
        await AddWebhookAsync("disabled", "http://disabled.lan/debarr", enabled: false);

        var deliveries = await SendAsync();

        Assert.Equal(
            [(automationId, "automation", Now, TimeSpan.Zero, new DeliveryOutcome.Succeeded()), (lightsId, "lights", Now, TimeSpan.Zero, new DeliveryOutcome.Succeeded())],
            deliveries.Select(Describe).OrderBy(delivery => delivery.NotifierName));
        Assert.Equal(["http://automation.lan/debarr", "http://lights.lan/debarr"], RequestedUrls.Order());
    }

    [Fact]
    public async Task A_failed_read_of_the_notifiers_completes_with_no_deliveries()
    {
        await AddWebhookAsync("automation", "http://automation.lan/debarr");
        await using (var connection = new SqliteConnection(_host.UnpooledConnectionString))
        {
            await connection.OpenAsync(CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "drop table fi_events";
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        var deliveries = await SendAsync();

        Assert.Empty(deliveries);
        Assert.Empty(RequestedUrls);
    }

    [Fact]
    public async Task A_notifier_that_hangs_times_out_after_5_seconds_while_a_prompt_one_succeeds()
    {
        var hangingId = await AddWebhookAsync("hanging", "http://hanging.lan/debarr");
        var promptId = await AddWebhookAsync("prompt", "http://prompt.lan/debarr");
        _handler.Respond = async (request, _, cancellationToken) =>
        {
            if (request.RequestUri!.Host == "hanging.lan")
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        var arrived = new ConcurrentQueue<Delivery>();

        var publishing = SendAsync(arrived);
        await Poll.UntilAsync(() => arrived.Count == 1 && RequestedUrls.Contains("http://hanging.lan/debarr"));
        _timeProvider.Advance(TimeSpan.FromSeconds(5));
        var deliveries = await publishing;

        Assert.Equal(
            [(promptId, TimeSpan.Zero, new DeliveryOutcome.Succeeded()), (hangingId, TimeSpan.FromSeconds(5), new DeliveryOutcome.Failed("Timed out after 5 s."))],
            deliveries.Select(delivery => (delivery.NotifierId, delivery.Duration, delivery.Outcome)));
    }

    [Fact]
    public async Task Each_delivery_carries_its_outcome_and_a_failure_its_error()
    {
        var automationId = await AddWebhookAsync("automation", "http://automation.lan/debarr");
        var unavailableId = await AddWebhookAsync("unavailable", "http://unavailable.lan/debarr");
        _handler.Respond = (request, _, _) => Task.FromResult(new HttpResponseMessage(
            request.RequestUri!.Host == "unavailable.lan" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));

        var deliveries = await SendAsync();

        Assert.Equal(
            [
                (automationId, new DeliveryOutcome.Succeeded()),
                (unavailableId, new DeliveryOutcome.Failed("The webhook answered 503 Service Unavailable.")),
            ],
            deliveries.OrderBy(delivery => delivery.NotifierName).Select(delivery => (delivery.NotifierId, delivery.Outcome)));
    }

    [Fact]
    public async Task Cancelling_ends_the_attempt_in_flight_and_yields_it_as_cancelled()
    {
        var automationId = await AddWebhookAsync("automation", "http://automation.lan/debarr");
        _handler.Respond = async (_, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

        var publishing = SendAsync(cancellationToken: cancellation.Token);
        await Poll.UntilAsync(() => !_handler.Requests.IsEmpty);
        await cancellation.CancelAsync();
        var delivery = Assert.Single(await publishing);

        Assert.Equal((automationId, new DeliveryOutcome.Cancelled()), (delivery.NotifierId, delivery.Outcome));
    }

    [Fact]
    public async Task A_test_posts_a_sample_from_player_debarr_test()
    {
        var automationId = await AddWebhookAsync("automation", "http://automation.lan/debarr");

        var delivery = await _publisher.TestAsync(
            automationId, "automation", WebhookSettings.Create("http://automation.lan/debarr", WebhookMethod.Post, []).Value,
            CancellationToken);

        Assert.Equal((automationId, "automation", Now, TimeSpan.Zero, new DeliveryOutcome.Succeeded()), Describe(delivery));
        Assert.Equal(
            [("POST", """{"player":"debarr-test","occurred_at":"2026-09-14T19:04:11.482Z","aspect_ratio":2.39,"source":"detected"}""")],
            _handler.Requests.Select(request => (request.Request.Method.Method, request.Body)));
    }

    [Fact]
    public async Task A_test_reaches_an_unsaved_notifier_and_reports_its_failure()
    {
        _handler.Respond = (_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var unsavedId = Guid.NewGuid();

        var delivery = await _publisher.TestAsync(
            unsavedId, "unsaved", WebhookSettings.Create("http://unsaved.lan/debarr", WebhookMethod.Post, []).Value,
            CancellationToken);

        Assert.Equal((unsavedId, "unsaved", Now, TimeSpan.Zero, new DeliveryOutcome.Failed("The webhook answered 401 Unauthorized.")), Describe(delivery));
        Assert.Equal(["http://unsaved.lan/debarr"], RequestedUrls);
    }

    private static (Guid NotifierId, string NotifierName, DateTimeOffset StartedAt, TimeSpan Duration, DeliveryOutcome Outcome) Describe(Delivery delivery) =>
        (delivery.NotifierId, delivery.NotifierName, delivery.StartedAt, delivery.Duration, delivery.Outcome);

    /// <summary>Sends the sample notification and returns every delivery as its attempt ends, adding each to <paramref name="arrived"/> as it ends.</summary>
    private async Task<List<Delivery>> SendAsync(ConcurrentQueue<Delivery>? arrived = null, CancellationToken? cancellationToken = null)
    {
        var deliveries = new List<Delivery>();
        await foreach (var attempt in Task.WhenEach(await _publisher.SendAsync(NotificationTests.Sample, cancellationToken ?? CancellationToken)))
        {
            var delivery = await attempt;
            arrived?.Enqueue(delivery);
            deliveries.Add(delivery);
        }

        return deliveries;
    }

    private async Task<Guid> AddWebhookAsync(string name, string url, bool enabled = true)
    {
        var notifier = new SaveNotifier(Guid.NewGuid(), name, enabled, WebhookSettings.Create(url, WebhookMethod.Post, []).Value);
        Assert.True((await _host.Services.GetRequiredService<IWolverineRuntime>().SendCommandAsync(notifier, CancellationToken)).IsSuccess);
        return notifier.NotifierId;
    }
}
