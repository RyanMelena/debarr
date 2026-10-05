using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Debarr.Hosting;

namespace Debarr.Components.Pages.Settings;

/// <summary>The host settings as one configuration holds them, or as the operator is editing them.</summary>
public sealed class GeneralSettingsForm
{
    public const string BindAddressKey = "Server:BindAddress";
    public const string PortKey = "Server:Port";
    public const string UrlBaseKey = "Server:UrlBase";
    public const string LogLevelKey = "Logging:LogLevel:Default";
    public const string FfmpegPathKey = "Ffmpeg:FfmpegPath";
    public const string FfprobePathKey = "Ffmpeg:FfprobePath";

    public string BindAddress { get; set; } = "";

    [Range(1, 65535, ErrorMessage = "The port must be between 1 and 65535.")]
    public int Port { get; set; }

    public string UrlBase { get; set; } = "";

    public LogLevel LogLevel { get; set; }

    public string FfmpegPath { get; set; } = "";

    public string FfprobePath { get; set; } = "";

    /// <summary>The host settings <paramref name="configuration"/> holds, with the option classes' defaults for absent keys.</summary>
    public static GeneralSettingsForm FromConfiguration(IConfiguration configuration)
    {
        var server = new ServerOptions();
        configuration.GetSection(ServerOptions.SectionName).Bind(server);

        var ffmpeg = new FfmpegOptions();
        configuration.GetSection(FfmpegOptions.SectionName).Bind(ffmpeg);

        return new GeneralSettingsForm
        {
            BindAddress = server.BindAddress,
            Port = server.Port,
            UrlBase = ServerOptions.NormalizeUrlBase(server.UrlBase),
            LogLevel = Enum.TryParse<LogLevel>(configuration[LogLevelKey], ignoreCase: true, out var level) && Enum.IsDefined(level)
                ? level
                : LogLevel.Information,
            FfmpegPath = ffmpeg.FfmpegPath,
            FfprobePath = ffmpeg.FfprobePath,
        };
    }

    public GeneralSettingsForm Copy() => new()
    {
        BindAddress = BindAddress,
        Port = Port,
        UrlBase = UrlBase,
        LogLevel = LogLevel,
        FfmpegPath = FfmpegPath,
        FfprobePath = FfprobePath,
    };

    /// <summary>The configuration keys this form sets to a different value than <paramref name="original"/>, with the values to save.</summary>
    public IReadOnlyDictionary<string, JsonNode> GetChanges(GeneralSettingsForm original)
    {
        var changes = new Dictionary<string, JsonNode>();

        AddText(changes, BindAddressKey, BindAddress.Trim(), original.BindAddress.Trim());
        AddText(changes, UrlBaseKey, ServerOptions.NormalizeUrlBase(UrlBase.Trim()), ServerOptions.NormalizeUrlBase(original.UrlBase.Trim()));
        AddText(changes, FfmpegPathKey, FfmpegPath.Trim(), original.FfmpegPath.Trim());
        AddText(changes, FfprobePathKey, FfprobePath.Trim(), original.FfprobePath.Trim());

        if (Port != original.Port)
        {
            changes[PortKey] = JsonValue.Create(Port);
        }

        if (LogLevel != original.LogLevel)
        {
            changes[LogLevelKey] = JsonValue.Create(LogLevel.ToString())!;
        }

        return changes;
    }

    private static void AddText(Dictionary<string, JsonNode> changes, string key, string value, string original)
    {
        if (value != original)
        {
            changes[key] = JsonValue.Create(value)!;
        }
    }
}
