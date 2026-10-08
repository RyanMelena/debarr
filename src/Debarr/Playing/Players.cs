using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Fisher;
using FluentResults;

namespace Debarr.Playing;

/// <summary>Every media player Debarr connects to, and the ids of the players removed.</summary>
public sealed record Players(IReadOnlyList<Player> All, IReadOnlyList<Guid> RemovedIds)
{
    /// <summary>The one stream every player's events are appended to.</summary>
    public static readonly Guid StreamId = new("b3a9e1d4-6c2f-4f7a-8e05-1d9c4b7a2e68");

    /// <summary>The players before their first event.</summary>
    public static Players Empty { get; } = new([], []);

    public static async Task<Players> ReadAsync(IQuerySession session, CancellationToken cancellationToken) =>
        await session.Events.FetchLatest<Players>(StreamId, cancellationToken) ?? Empty;

    /// <summary>The stream's id, which the event store keys the aggregate on.</summary>
    public Guid Id => StreamId;

    public Player? Find(Guid id) => All.FirstOrDefault(player => player.Id == id);

    public bool WasRemoved(Guid id) => RemovedIds.Contains(id);

    public static Players Create(PlayerAdded added) => Empty.Apply(added);

    public Players Apply(PlayerAdded added) =>
        this with { All = [.. All, new Player(added.PlayerId, added.Name, added.Enabled, added.Endpoint, added.PathMappings, added.ExcludedPaths ?? [])] };

    public Players Apply(PlayerChanged changed) =>
        WithPlayer(changed.PlayerId, player => player with { Enabled = changed.Enabled, Endpoint = changed.Endpoint, PathMappings = changed.PathMappings, ExcludedPaths = changed.ExcludedPaths ?? [] });

    public Players Apply(PlayerRenamed renamed) => WithPlayer(renamed.PlayerId, player => player with { Name = renamed.Name });

    public Players Apply(PlayerRemoved removed) =>
        this with { All = [.. All.Where(player => player.Id != removed.PlayerId)], RemovedIds = [.. RemovedIds, removed.PlayerId] };

    private Players WithPlayer(Guid id, Func<Player, Player> change) =>
        this with { All = [.. All.Select(player => player.Id == id ? change(player) : player)] };
}

/// <summary>A media player Debarr connects to: its unique name, whether it is enabled, its type's endpoint, its path mappings and its excluded paths.</summary>
/// <param name="ExcludedPaths">Player paths whose playback Debarr ignores, canonical under the player type's rules.</param>
public sealed record Player(Guid Id, string Name, bool Enabled, PlayerEndpoint Endpoint, IReadOnlyList<PathMapping> PathMappings, IReadOnlyList<PlayerPath> ExcludedPaths)
{
    /// <summary>Whether an excluded path covers <paramref name="path"/> by more whole segments than every path mapping that covers it.</summary>
    public bool Excludes(PlayerPath path) =>
        LongestCovering(ExcludedPaths, path) > LongestCovering(PathMappings.Select(pathMapping => pathMapping.PlayerPath), path);

    private static int LongestCovering(IEnumerable<PlayerPath> playerPaths, PlayerPath path) =>
        playerPaths.Where(playerPath => playerPath.Covers(path)).Select(playerPath => playerPath.Value.Length).DefaultIfEmpty(-1).Max();
}

/// <summary>Where a player of one type listens, and how that type writes the paths it reports; its type is the player's type.</summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(KodiEndpoint), "kodi")]
public abstract record PlayerEndpoint
{
    /// <summary>The path in the canonical form of this player type's paths, so it matches the paths the player reports by whole segments.</summary>
    public abstract PlayerPath ToPlayerPath(string path);

    /// <summary>A field error for each value outside its bounds.</summary>
    public abstract Result Validate();
}

/// <summary>Where a Kodi player's JSON-RPC listens, how often a connection pings it, and how long a request may take.</summary>
public sealed partial record KodiEndpoint : PlayerEndpoint
{
    public const int DefaultPort = 9090;

    public const int DefaultPingIntervalSeconds = 60;

    public const int DefaultRequestTimeoutSeconds = 10;

    private const string StackPrefix = "stack://";

    // Kodi joins the parts of a stack with a spaced comma.
    private const string StackSeparator = " , ";

    [JsonConstructor]
    private KodiEndpoint(string host, int port, int pingIntervalSeconds, int requestTimeoutSeconds)
    {
        Host = host;
        Port = port;
        PingIntervalSeconds = pingIntervalSeconds;
        RequestTimeoutSeconds = requestTimeoutSeconds;
    }

    public string Host { get; }

    public int Port { get; }

    public int PingIntervalSeconds { get; }

    public int RequestTimeoutSeconds { get; }

    /// <summary>The endpoint, or a field error for each value outside its bounds.</summary>
    public static Result<KodiEndpoint> Create(string host, int port, int pingIntervalSeconds, int requestTimeoutSeconds)
    {
        var endpoint = new KodiEndpoint(host, port, pingIntervalSeconds, requestTimeoutSeconds);
        return endpoint.Validate() is { IsFailed: true } refused ? refused.ToResult<KodiEndpoint>() : endpoint;
    }

    public override Result Validate()
    {
        var errors = new List<IError>();
        if (string.IsNullOrWhiteSpace(Host))
        {
            errors.Add(new FieldError(nameof(Host), "Enter the host."));
        }

        if (Port is < 1 or > 65535)
        {
            errors.Add(new FieldError(nameof(Port), "Enter 1 to 65535."));
        }

        if (PingIntervalSeconds < 1)
        {
            errors.Add(new FieldError(nameof(PingIntervalSeconds), "Enter 1 or more."));
        }

        if (RequestTimeoutSeconds < 1)
        {
            errors.Add(new FieldError(nameof(RequestTimeoutSeconds), "Enter 1 or more."));
        }

        return errors.Count > 0 ? Result.Fail(errors) : Result.Ok();
    }

    public override PlayerPath ToPlayerPath(string path) => ToKodiPlayerPath(path);

    /// <summary>
    /// The path Kodi reports, canonicalised: percent escapes decoded, a stack reduced to its first part,
    /// backslashes written as forward slashes when the path has a Windows root, repeated and trailing separators removed,
    /// and Unicode composed.
    /// </summary>
    public static PlayerPath ToKodiPlayerPath(string path)
    {
        var value = Uri.UnescapeDataString(path);

        // A stack part can itself be a stack.
        while (value.StartsWith(StackPrefix, StringComparison.Ordinal))
        {
            var parts = value[StackPrefix.Length..];
            var separator = parts.IndexOf(StackSeparator, StringComparison.Ordinal);
            value = Uri.UnescapeDataString(separator < 0 ? parts : parts[..separator]);
        }

        // A backslash is a legal character in a Linux file name, so it is a separator only under a Windows root.
        if (WindowsRoot().IsMatch(value))
        {
            value = value.Replace('\\', '/');
        }

        var rootLength = SchemeOrUncRoot().Match(value).Length;
        var canonical = value[..rootLength] + RepeatedSeparators().Replace(value[rootLength..], "/").TrimEnd('/');

        return new PlayerPath((canonical.Length == 0 ? "/" : canonical).Normalize(NormalizationForm.FormC));
    }

    [GeneratedRegex(@"^(?:\\\\|[A-Za-z]:[\\/])")]
    private static partial Regex WindowsRoot();

    [GeneratedRegex("^(?:[A-Za-z][A-Za-z0-9+.-]*://|//)")]
    private static partial Regex SchemeOrUncRoot();

    [GeneratedRegex("/{2,}")]
    private static partial Regex RepeatedSeparators();
}
