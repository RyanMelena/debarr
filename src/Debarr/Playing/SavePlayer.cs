using Debarr.Scanning;
using FluentResults;
using Wolverine.Persistence.EventSourcing;

namespace Debarr.Playing;

/// <param name="PlayerId">The player's id; a new player's id comes from its form.</param>
/// <param name="PathMappings">The path mappings as entered, before their player paths are canonicalised.</param>
/// <param name="ExcludedPaths">The excluded paths as entered, before they are canonicalised.</param>
public sealed record SavePlayer(Guid PlayerId, string Name, bool Enabled, PlayerEndpoint Endpoint, IReadOnlyList<PathMappingEntry> PathMappings, IReadOnlyList<string> ExcludedPaths)
{
    /// <summary>The players' stream, which Wolverine loads the aggregate from.</summary>
    public Guid PlayersId => Players.StreamId;

    /// <summary>The field that edits one path of the path mapping at <paramref name="index"/>, such as <c>PathMappings[0].PlayerPath</c>.</summary>
    public static string PathMappingField(int index, string path) => $"{nameof(PathMappings)}[{index}].{path}";

    /// <summary>The field that edits the excluded path at <paramref name="index"/>, such as <c>ExcludedPaths[0]</c>.</summary>
    public static string ExcludedPathField(int index) => $"{nameof(ExcludedPaths)}[{index}]";
}

public static class SavePlayerHandler
{
    /// <summary>
    /// Refuses a save of a player that was removed or that changes its type,
    /// and refuses beneath its field a blank name or one another player has, an endpoint value outside its bounds,
    /// a path mapping with a blank path or with the canonical player path of another,
    /// and an excluded path that is blank or whose canonical path another excluded path or a path mapping has.
    /// </summary>
    public static Result Validate(SavePlayer command, Players? players)
    {
        players ??= Players.Empty;
        if (players.WasRemoved(command.PlayerId))
        {
            return Result.Fail($"The player {command.Name} was removed.");
        }

        if (players.Find(command.PlayerId) is { } player && player.Endpoint.GetType() != command.Endpoint.GetType())
        {
            return Result.Fail($"The player {player.Name} keeps its type.");
        }

        List<IError> errors =
        [
            .. ValidateName(command.PlayerId, command.Name, players),
            .. command.Endpoint.Validate().Errors,
            .. ValidatePathMappings(command.PathMappings, command.Endpoint.ToPlayerPath),
            .. ValidateExcludedPaths(command.ExcludedPaths, command.PathMappings, command.Endpoint.ToPlayerPath),
        ];
        return errors.Count > 0 ? Result.Fail(errors) : Result.Ok();
    }

    /// <summary>Refuses a blank name or one another player has.</summary>
    public static IEnumerable<FieldError> ValidateName(Guid playerId, string name, Players players)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            yield return new FieldError(nameof(SavePlayer.Name), "Enter a name.");
        }
        else if (players.All.Any(other => other.Id != playerId && string.Equals(other.Name, name, StringComparison.Ordinal)))
        {
            yield return new FieldError(nameof(SavePlayer.Name), $"A player named {name} already exists.");
        }
    }

    /// <summary>Adds a new player, and otherwise changes its settings, after renaming it when its name changed.</summary>
    public static IReadOnlyList<object> Handle(SavePlayer command, [WriteModel(Required = false)] Players? players)
    {
        var pathMappings = ToPathMappings(command);
        List<PlayerPath> excludedPaths = [.. command.ExcludedPaths.Select(command.Endpoint.ToPlayerPath)];
        return (players ?? Players.Empty).Find(command.PlayerId) switch
        {
            null => [new PlayerAdded(command.PlayerId, command.Name, command.Enabled, command.Endpoint, pathMappings, excludedPaths)],
            { Name: var name } when name == command.Name => [new PlayerChanged(command.PlayerId, command.Enabled, command.Endpoint, pathMappings, excludedPaths)],
            _ =>
            [
                new PlayerRenamed(command.PlayerId, command.Name),
                new PlayerChanged(command.PlayerId, command.Enabled, command.Endpoint, pathMappings, excludedPaths),
            ],
        };
    }

    /// <summary>Refuses a path mapping with a blank path or with the canonical player path of another.</summary>
    public static IEnumerable<FieldError> ValidatePathMappings(IReadOnlyList<PathMappingEntry> pathMappings, Func<string, PlayerPath> toPlayerPath)
    {
        var playerPaths = pathMappings.Select(entry => toPlayerPath(entry.PlayerPath)).ToList();
        var duplicates = playerPaths.CountBy(playerPath => playerPath).Where(count => count.Value > 1).Select(count => count.Key).ToHashSet();
        for (var index = 0; index < pathMappings.Count; index++)
        {
            var entry = pathMappings[index];
            if (string.IsNullOrWhiteSpace(entry.PlayerPath))
            {
                yield return new FieldError(SavePlayer.PathMappingField(index, nameof(PathMappingEntry.PlayerPath)), "Enter the player path.");
            }
            else if (duplicates.Contains(playerPaths[index]))
            {
                yield return new FieldError(SavePlayer.PathMappingField(index, nameof(PathMappingEntry.PlayerPath)), $"{playerPaths[index].Value} has another path mapping.");
            }

            if (string.IsNullOrWhiteSpace(entry.LocalPath))
            {
                yield return new FieldError(SavePlayer.PathMappingField(index, nameof(PathMappingEntry.LocalPath)), "Enter the local path.");
            }
        }
    }

    /// <summary>Refuses an excluded path that is blank or whose canonical path another excluded path or a path mapping has.</summary>
    public static IEnumerable<FieldError> ValidateExcludedPaths(IReadOnlyList<string> excludedPaths, IReadOnlyList<PathMappingEntry> pathMappings, Func<string, PlayerPath> toPlayerPath)
    {
        var playerPaths = excludedPaths.Select(toPlayerPath).ToList();
        var duplicates = playerPaths.CountBy(playerPath => playerPath).Where(count => count.Value > 1).Select(count => count.Key).ToHashSet();
        var mappedPlayerPaths = pathMappings.Where(entry => !string.IsNullOrWhiteSpace(entry.PlayerPath)).Select(entry => toPlayerPath(entry.PlayerPath)).ToHashSet();
        for (var index = 0; index < excludedPaths.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(excludedPaths[index]))
            {
                yield return new FieldError(SavePlayer.ExcludedPathField(index), "Enter the player path.");
            }
            else if (duplicates.Contains(playerPaths[index]))
            {
                yield return new FieldError(SavePlayer.ExcludedPathField(index), $"{playerPaths[index].Value} is excluded already.");
            }
            else if (mappedPlayerPaths.Contains(playerPaths[index]))
            {
                yield return new FieldError(SavePlayer.ExcludedPathField(index), $"{playerPaths[index].Value} has a path mapping.");
            }
        }
    }

    private static List<PathMapping> ToPathMappings(SavePlayer command) =>
        [.. command.PathMappings.Select(entry => new PathMapping(command.Endpoint.ToPlayerPath(entry.PlayerPath), new LocalPath(entry.LocalPath)))];
}
