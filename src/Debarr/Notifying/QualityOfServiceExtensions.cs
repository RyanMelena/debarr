using MQTTnet.Protocol;

namespace Debarr.Notifying;

public static class QualityOfServiceExtensions
{
    public static MqttQualityOfServiceLevel ToMqttQualityOfServiceLevel(this QualityOfService qos) => qos switch
    {
        QualityOfService.AtMostOnce => MqttQualityOfServiceLevel.AtMostOnce,
        QualityOfService.AtLeastOnce => MqttQualityOfServiceLevel.AtLeastOnce,
        QualityOfService.ExactlyOnce => MqttQualityOfServiceLevel.ExactlyOnce,
        _ => throw new ArgumentOutOfRangeException(nameof(qos), qos, null),
    };
}
