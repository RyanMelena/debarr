using Debarr.Playing;
using Fisher;
using MQTTnet;

namespace Debarr.Notifying;

/// <summary>Publishes notifications to every enabled notifier.</summary>
public sealed partial class NotificationPublisher(
    IDocumentStore store,
    IHttpClientFactory httpClientFactory,
    MqttClientFactory mqttClientFactory,
    TimeProvider timeProvider,
    ILogger<NotificationPublisher> logger)
{
    /// <summary>The budget for one delivery attempt.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private const string TestPlayerName = "debarr-test";

    /// <summary>
    /// Starts sending the notification to every enabled notifier at once, and returns each attempt, which ends with its delivery.
    /// Cancelling ends the attempts in flight, each with a cancelled delivery.
    /// </summary>
    public async Task<IReadOnlyList<Task<Delivery>>> SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        var notifiers = await GetEnabledNotifiersAsync();
        return [.. notifiers.Select(notifier => AttemptAsync(notifier, notification, cancellationToken))];
    }

    /// <summary>
    /// Sends a sample notification from player debarr-test, a detected 2.39 occurring now, with the notifier's values, saved or not, under the same timeout.
    /// The delivery stays out of the history.
    /// </summary>
    public Task<Delivery> TestAsync(Guid notifierId, string name, NotifierSettings settings, CancellationToken cancellationToken) =>
        AttemptAsync(
            new Notifier(notifierId, name, true, settings),
            new Notification
            {
                Player = TestPlayerName,
                OccurredAt = timeProvider.GetUtcNow(),
                AspectRatio = 2.39,
                Source = NotificationAspectRatioSource.Detected,
            },
            cancellationToken);

    /// <summary>The enabled notifiers, or none when the read fails.</summary>
    /// <remarks>The read runs to the end after a newer playback cancels the notification, so each attempt starts and records its cancellation.</remarks>
    private async Task<IReadOnlyList<Notifier>> GetEnabledNotifiersAsync()
    {
        try
        {
            await using var session = store.QuerySession();
            return [.. (await Notifiers.ReadAsync(session, CancellationToken.None)).All.Where(notifier => notifier.Enabled)];
        }
        catch (Exception exception)
        {
            LogNotifiersUnread(exception);
            return [];
        }
    }

    /// <summary>Sends the notification through the notifier's client under the timeout, and logs the outcome.</summary>
    private async Task<Delivery> AttemptAsync(Notifier notifier, Notification notification, CancellationToken cancellationToken)
    {
        var client = CreateClient(notifier.Settings);
        var startedAt = timeProvider.GetUtcNow();
        var start = timeProvider.GetTimestamp();
        using var timeout = new CancellationTokenSource(Timeout, timeProvider);
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var result = await client.SendAsync(notification, attempt.Token);
            return result.IsSuccess
                ? Ended(new DeliveryOutcome.Succeeded())
                : Ended(new DeliveryOutcome.Failed(string.Join(" ", result.Errors.Select(error => error.Message))));
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            return Ended(new DeliveryOutcome.Cancelled());
        }
        catch (Exception) when (timeout.IsCancellationRequested)
        {
            return Ended(new DeliveryOutcome.Failed($"Timed out after {Timeout.TotalSeconds} s."));
        }
        catch (Exception exception)
        {
            // A notifier client reports its failures as results, so an exception here is a defect in the client.
            LogNotifierThrew(exception, notifier.Name);
            return Ended(new DeliveryOutcome.Failed(exception.Message));
        }

        // The delivery for the way the attempt ended, logged.
        Delivery Ended(DeliveryOutcome outcome)
        {
            var delivery = new Delivery(Guid.NewGuid(), notifier.Id, notifier.Name, startedAt, timeProvider.GetElapsedTime(start), outcome);
            switch (outcome)
            {
                case DeliveryOutcome.Succeeded:
                    LogDelivered(notifier.Name, (long)delivery.Duration.TotalMilliseconds);
                    break;
                case DeliveryOutcome.Failed failed:
                    LogDeliveryFailed(notifier.Name, failed.Error);
                    break;
                case DeliveryOutcome.Cancelled:
                    LogDeliveryCancelled(notifier.Name);
                    break;
            }

            return delivery;
        }
    }

    private INotifierClient CreateClient(NotifierSettings settings) => settings switch
    {
        MqttSettings mqtt => new MqttNotifierClient(mqtt, mqttClientFactory),
        WebhookSettings webhook => new WebhookNotifierClient(webhook, httpClientFactory),
        _ => throw new NotSupportedException($"The notifier settings {settings.GetType().Name} have no client."),
    };

    [LoggerMessage(LogLevel.Error, "Could not list the enabled notifiers.")]
    private partial void LogNotifiersUnread(Exception exception);

    [LoggerMessage(LogLevel.Error, "The {Notifier} notifier threw.")]
    private partial void LogNotifierThrew(Exception exception, string notifier);

    [LoggerMessage(LogLevel.Information, "Delivered to {Notifier} in {DurationMs} ms.")]
    private partial void LogDelivered(string notifier, long durationMs);

    [LoggerMessage(LogLevel.Warning, "Delivery to {Notifier} failed. {Error}")]
    private partial void LogDeliveryFailed(string notifier, string error);

    [LoggerMessage(LogLevel.Information, "Delivery to {Notifier} was cancelled.")]
    private partial void LogDeliveryCancelled(string notifier);
}
