using Debarr.Notifying;

namespace Debarr.Tests.Notifying;

public sealed class MqttBrokerAddressTests
{
    [Theory]
    [InlineData("mqtt://broker.lan", "broker.lan", 1883, false)]
    [InlineData("mqtt://broker.lan:1884", "broker.lan", 1884, false)]
    [InlineData("mqtt://broker.lan/", "broker.lan", 1883, false)]
    [InlineData(" MQTT://Broker.lan ", "broker.lan", 1883, false)]
    [InlineData("mqtts://broker.lan", "broker.lan", 8883, true)]
    [InlineData("mqtts://broker.lan:8884", "broker.lan", 8884, true)]
    [InlineData("mqtt://192.168.1.10:1883", "192.168.1.10", 1883, false)]
    [InlineData("mqtt://[::1]:1883", "::1", 1883, false)]
    public void A_url_resolves_to_a_host_a_port_and_a_transport(string url, string host, int port, bool tls)
    {
        var address = MqttBrokerAddress.Parse(url);

        Assert.True(address.IsSuccess);
        Assert.Equal(new MqttBrokerAddress(host, port, tls), address.Value);
    }

    [Theory]
    [InlineData("mqtt://broker.lan", "mqtt://broker.lan:1883")]
    [InlineData("mqtts://broker.lan:8884", "mqtts://broker.lan:8884")]
    [InlineData("mqtt://[::1]:1883", "mqtt://[::1]:1883")]
    public void An_address_reads_back_as_the_url_of_its_host_and_port(string url, string expected) =>
        Assert.Equal(expected, MqttBrokerAddress.Parse(url).Value.ToUrl());

    [Theory]
    [InlineData("broker.lan")]
    [InlineData("http://broker.lan")]
    [InlineData("ssl://broker.lan")]
    [InlineData("mqtt://")]
    [InlineData("mqtt://broker.lan:0x")]
    [InlineData("mqtt://broker.lan:65536")]
    [InlineData("mqtt://user:secret@broker.lan")]
    [InlineData("mqtt://broker.lan/topic")]
    [InlineData("mqtt://broker.lan?qos=1")]
    public void Anything_but_an_mqtt_or_mqtts_url_of_a_host_and_port_fails_naming_the_url(string url)
    {
        var address = MqttBrokerAddress.Parse(url);

        Assert.True(address.IsFailed);
        Assert.Equal($"\"{url}\" needs to be an mqtt or mqtts URL of a host and an optional port.", Assert.Single(address.Errors).Message);
    }
}
