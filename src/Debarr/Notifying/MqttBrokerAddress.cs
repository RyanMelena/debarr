using FluentResults;

namespace Debarr.Notifying;

/// <summary>The host, port and transport an MQTT URL resolves to.</summary>
public readonly record struct MqttBrokerAddress(string Host, int Port, bool Tls)
{
    private const int PlainPort = 1883;
    private const int TlsPort = 8883;

    /// <summary>
    /// Reads an <c>mqtt://</c> or <c>mqtts://</c> URL of a host and an optional port.
    /// mqtts turns TLS on, and each scheme has its standard port by default.
    /// </summary>
    public static Result<MqttBrokerAddress> Parse(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("mqtt" or "mqtts")
            || uri.IdnHost.Length == 0
            || uri.UserInfo.Length > 0
            || uri.PathAndQuery is not ("" or "/")
            || uri.Fragment.Length > 0)
        {
            return Result.Fail($"\"{url}\" needs to be an mqtt or mqtts URL of a host and an optional port.");
        }

        var tls = uri.Scheme == "mqtts";
        return new MqttBrokerAddress(uri.IdnHost, uri.IsDefaultPort ? (tls ? TlsPort : PlainPort) : uri.Port, tls);
    }

    /// <summary>The mqtt or mqtts URL of the host and port.</summary>
    public string ToUrl() => new UriBuilder(Tls ? "mqtts" : "mqtt", Host, Port).Uri.GetLeftPart(UriPartial.Authority);
}
