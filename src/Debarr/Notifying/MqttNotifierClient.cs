using System.Text.Json;
using Debarr.Playing;
using FluentResults;
using MQTTnet;

namespace Debarr.Notifying;

/// <summary>Connects to the broker, publishes one notification, and disconnects.</summary>
public sealed class MqttNotifierClient(MqttSettings settings, MqttClientFactory mqttClientFactory) : INotifierClient
{
    public async Task<Result> SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        var broker = settings.Broker;
        var options = new MqttClientOptionsBuilder().WithTcpServer(broker.Host, broker.Port);
        if (!string.IsNullOrWhiteSpace(settings.ClientId))
        {
            options = options.WithClientId(settings.ClientId);
        }

        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            options = options.WithCredentials(settings.Username, settings.Password ?? "");
        }

        if (broker.Tls)
        {
            options = options.WithTlsOptions(tls => tls.UseTls());
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(settings.TopicTemplate.Replace("{player}", notification.Player))
            .WithPayload(JsonSerializer.SerializeToUtf8Bytes(notification))
            .WithQualityOfServiceLevel(settings.Qos.ToMqttQualityOfServiceLevel())
            .WithRetainFlag(false)
            .Build();

        using var client = mqttClientFactory.CreateMqttClient();
        try
        {
            var connected = await client.ConnectAsync(options.Build(), cancellationToken);
            if (connected.ResultCode != MqttClientConnectResultCode.Success)
            {
                return Result.Fail($"The broker refused the connection: {connected.ResultCode}.");
            }

            var published = await client.PublishAsync(message, cancellationToken);
            if (!published.IsSuccess)
            {
                return Result.Fail($"The broker answered {published.ReasonCode}.");
            }

            await client.DisconnectAsync(cancellationToken: cancellationToken);
            return Result.Ok();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Fail(exception.Message);
        }
    }
}
