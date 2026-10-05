using Debarr.Playing;
using FluentResults;

namespace Debarr.Components.Pages.Settings;

/// <summary>A Kodi player and its path mappings as the Kodi modal edits them.</summary>
public sealed record KodiPlayerForm
{
    /// <summary>The player's id, which a new player's form generates.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>True for a player that is not saved yet.</summary>
    public bool IsNew { get; set; } = true;

    public string Name { get; set; } = "";

    public bool Enabled { get; set; }

    public string Host { get; set; } = "";

    public int Port { get; set; }

    public int PingIntervalSeconds { get; set; }

    public int RequestTimeoutSeconds { get; set; }

    public List<PathMappingForm> PathMappings { get; set; } = [];

    public static KodiPlayerForm ForNewPlayer() => new()
    {
        Enabled = true,
        Port = KodiEndpoint.DefaultPort,
        PingIntervalSeconds = KodiEndpoint.DefaultPingIntervalSeconds,
        RequestTimeoutSeconds = KodiEndpoint.DefaultRequestTimeoutSeconds,
    };

    public static KodiPlayerForm FromKodiPlayer(Player player, KodiEndpoint endpoint) => new()
    {
        Id = player.Id,
        IsNew = false,
        Name = player.Name,
        Enabled = player.Enabled,
        Host = endpoint.Host,
        Port = endpoint.Port,
        PingIntervalSeconds = endpoint.PingIntervalSeconds,
        RequestTimeoutSeconds = endpoint.RequestTimeoutSeconds,
        PathMappings = [.. player.PathMappings
            .OrderBy(pathMapping => pathMapping.PlayerPath.Value, StringComparer.Ordinal)
            .Select(PathMappingForm.FromPathMapping)],
    };

    /// <summary>A copy whose path mappings edit apart from this form's.</summary>
    public KodiPlayerForm Copy() => this with { PathMappings = [.. PathMappings.Select(pathMapping => pathMapping.Copy())] };

    /// <summary>Whether a value or a path mapping differs from <paramref name="saved"/>.</summary>
    public bool HasChangesFrom(KodiPlayerForm saved) =>
        this with { PathMappings = saved.PathMappings } != saved || !PathMappings.Select(Paths).SequenceEqual(saved.PathMappings.Select(Paths));

    private static (string PlayerPath, string LocalPath) Paths(PathMappingForm pathMapping) => (pathMapping.PlayerPath, pathMapping.LocalPath);

    /// <summary>
    /// The save of the trimmed values, or a field error for each endpoint value outside its bounds
    /// together with the name's, checked against <paramref name="players"/>, and the path mappings' field errors.
    /// </summary>
    public Result<SavePlayer> ToSavePlayer(Players players)
    {
        var name = Name.Trim();
        List<PathMappingEntry> pathMappings = [.. PathMappings.Select(pathMapping => new PathMappingEntry(pathMapping.PlayerPath.Trim(), pathMapping.LocalPath.Trim()))];
        var endpoint = KodiEndpoint.Create(Host.Trim(), Port, PingIntervalSeconds, RequestTimeoutSeconds);
        return endpoint.IsSuccess
            ? new SavePlayer(Id, name, Enabled, endpoint.Value, pathMappings)
            : Result.Fail(
            [
                .. SavePlayerHandler.ValidateName(Id, name, players),
                .. endpoint.Errors,
                .. SavePlayerHandler.ValidatePathMappings(pathMappings, KodiEndpoint.ToKodiPlayerPath),
            ]);
    }
}
