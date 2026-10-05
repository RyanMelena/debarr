using System.ComponentModel.DataAnnotations;

namespace Debarr.Hosting;

/// <summary>The web server's host settings, bound from DEBARR__SERVER__: the bind address, the port and the URL base.</summary>
public sealed class ServerOptions
{
    public const string SectionName = "Server";

    public string BindAddress { get; set; } = "*";

    [Range(1, 65535)]
    public int Port { get; set; } = 8080;

    // "" or "/segment" after post-configuration.
    public string UrlBase { get; set; } = "";

    /// <summary>The URL base as "" for the root, or a single leading slash and no trailing slash.</summary>
    public static string NormalizeUrlBase(string urlBase)
    {
        var segment = urlBase.Trim('/');
        return segment.Length == 0 ? "" : "/" + segment;
    }
}
