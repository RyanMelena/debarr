using System.Text.Json;
using Debarr.Playing;
using Debarr.Scanning;
using FluentResults;

namespace Debarr.Tests.Playing;

public sealed class SavePlayerTests
{
    private static readonly Guid TheaterId = Guid.NewGuid();

    private static readonly Guid BedroomId = Guid.NewGuid();

    private static readonly KodiEndpoint Endpoint = KodiEndpoint.Create("kodi.lan", 9090, 60, 10).Value;

    /// <summary>Theater and Bedroom.</summary>
    private static readonly Players Stored = Players.Create(new PlayerAdded(TheaterId, "Theater", true, Endpoint, []))
        .Apply(new PlayerAdded(BedroomId, "Bedroom", true, KodiEndpoint.Create("bedroom.lan", 9090, 60, 10).Value, []));

    [Fact]
    public void A_new_player_is_added_with_its_player_paths_canonicalised()
    {
        var command = new SavePlayer(TheaterId, "Theater", true, Endpoint, [new PathMappingEntry("smb://nas/My%20Media//movies/", "/media/movies")]);

        Assert.True(SavePlayerHandler.Validate(command, null).IsSuccess);
        var added = Assert.IsType<PlayerAdded>(Assert.Single(SavePlayerHandler.Handle(command, null)));
        Assert.Equal((TheaterId, "Theater", true, (PlayerEndpoint)Endpoint), (added.PlayerId, added.Name, added.Enabled, added.Endpoint));
        Assert.Equal([new PathMapping(new PlayerPath("smb://nas/My Media/movies"), new LocalPath("/media/movies"))], added.PathMappings);
    }

    [Fact]
    public void A_save_under_the_same_name_changes_the_settings()
    {
        var endpoint = KodiEndpoint.Create("kodi.lan", 9091, 60, 10).Value;
        var command = new SavePlayer(TheaterId, "Theater", false, endpoint, []);

        Assert.True(SavePlayerHandler.Validate(command, Stored).IsSuccess);
        var changed = Assert.IsType<PlayerChanged>(Assert.Single(SavePlayerHandler.Handle(command, Stored)));
        Assert.Equal((TheaterId, false, (PlayerEndpoint)endpoint), (changed.PlayerId, changed.Enabled, changed.Endpoint));
    }

    [Fact]
    public void A_save_under_a_new_name_renames_the_player_and_changes_its_settings()
    {
        var command = new SavePlayer(TheaterId, "Lounge", true, Endpoint, []);

        Assert.True(SavePlayerHandler.Validate(command, Stored).IsSuccess);
        var events = SavePlayerHandler.Handle(command, Stored);
        Assert.Equal(2, events.Count);
        Assert.Equal(new PlayerRenamed(TheaterId, "Lounge"), events[0]);
        Assert.Equal(TheaterId, Assert.IsType<PlayerChanged>(events[1]).PlayerId);
    }

    [Fact]
    public void A_name_another_player_has_is_refused_beneath_its_field()
    {
        var added = new SavePlayer(Guid.NewGuid(), "Theater", true, Endpoint, []);
        var renamed = new SavePlayer(TheaterId, "Bedroom", true, Endpoint, []);

        foreach (var command in new[] { added, renamed })
        {
            var error = Assert.IsType<FieldError>(Assert.Single(SavePlayerHandler.Validate(command, Stored).Errors));
            Assert.Equal((nameof(SavePlayer.Name), $"A player named {command.Name} already exists."), (error.Field, error.Message));
        }
    }

    [Fact]
    public void A_rename_frees_the_old_name_for_another_player()
    {
        var players = Stored.Apply(new PlayerRenamed(TheaterId, "Lounge"));

        Assert.True(SavePlayerHandler.Validate(new SavePlayer(BedroomId, "Theater", true, Endpoint, []), players).IsSuccess);
    }

    [Fact]
    public void A_removal_frees_the_name()
    {
        var players = Stored.Apply(new PlayerRemoved(TheaterId));

        Assert.True(SavePlayerHandler.Validate(new SavePlayer(Guid.NewGuid(), "Theater", true, Endpoint, []), players).IsSuccess);
    }

    [Fact]
    public void A_name_that_differs_from_another_player_only_in_case_is_accepted()
    {
        var command = new SavePlayer(Guid.NewGuid(), "theater", true, Endpoint, []);

        Assert.True(SavePlayerHandler.Validate(command, Stored).IsSuccess);
    }

    [Fact]
    public void A_save_of_a_removed_player_is_refused()
    {
        var command = new SavePlayer(TheaterId, "Theater", true, Endpoint, []);

        Assert.Equal("The player Theater was removed.", Assert.Single(SavePlayerHandler.Validate(command, Stored.Apply(new PlayerRemoved(TheaterId))).Errors).Message);
    }

    [Fact]
    public void Two_path_mappings_with_one_canonical_player_path_are_each_refused_beneath_their_row()
    {
        var command = new SavePlayer(
            TheaterId,
            "Theater",
            true,
            Endpoint,
            [new PathMappingEntry("smb://nas/media", "/media"), new PathMappingEntry("smb://nas/tv", "/tv"), new PathMappingEntry("smb://nas//media/", "/mnt/media")]);

        Assert.Equal(
            [
                ("PathMappings[0].PlayerPath", "smb://nas/media has another path mapping."),
                ("PathMappings[2].PlayerPath", "smb://nas/media has another path mapping."),
            ],
            FieldErrors(SavePlayerHandler.Validate(command, null)));
    }

    [Fact]
    public void A_path_mapping_with_a_blank_path_is_refused_beneath_that_path()
    {
        var command = new SavePlayer(TheaterId, "Theater", true, Endpoint, [new PathMappingEntry("smb://nas/media", " "), new PathMappingEntry("", "/tv")]);

        Assert.Equal(
            [
                ("PathMappings[0].LocalPath", "Enter the local path."),
                ("PathMappings[1].PlayerPath", "Enter the player path."),
            ],
            FieldErrors(SavePlayerHandler.Validate(command, null)));
    }

    [Fact]
    public void An_endpoint_read_with_port_0_is_refused_beneath_the_port_field()
    {
        var endpoint = JsonSerializer.Deserialize<PlayerEndpoint>(
            """{"$type":"kodi","host":"kodi.lan","port":0,"pingIntervalSeconds":60,"requestTimeoutSeconds":10}""",
            JsonSerializerOptions.Web)!;

        var command = new SavePlayer(TheaterId, "Theater", true, endpoint, []);

        Assert.Equal([("Port", "Enter 1 to 65535.")], FieldErrors(SavePlayerHandler.Validate(command, null)));
    }

    [Fact]
    public void Saving_a_path_mapping_again_with_its_player_path_changes_its_local_path()
    {
        var players = Stored.Apply(new PlayerChanged(TheaterId, true, Endpoint, [new PathMapping(new PlayerPath("smb://nas/media"), new LocalPath("/media"))]));
        var command = new SavePlayer(TheaterId, "Theater", true, Endpoint, [new PathMappingEntry("smb://nas/media", "/mnt/media")]);

        Assert.True(SavePlayerHandler.Validate(command, players).IsSuccess);
        var changed = Assert.IsType<PlayerChanged>(Assert.Single(SavePlayerHandler.Handle(command, players)));
        Assert.Equal([new PathMapping(new PlayerPath("smb://nas/media"), new LocalPath("/mnt/media"))], changed.PathMappings);
    }

    [Fact]
    public void A_save_that_changes_the_player_type_is_refused()
    {
        var command = new SavePlayer(TheaterId, "Theater", true, new OtherEndpoint(), []);

        Assert.Equal("The player Theater keeps its type.", Assert.Single(SavePlayerHandler.Validate(command, Stored).Errors).Message);
    }

    [Fact]
    public void A_blank_name_is_refused_beneath_its_field()
    {
        var command = new SavePlayer(TheaterId, " ", true, Endpoint, []);

        var error = Assert.IsType<FieldError>(Assert.Single(SavePlayerHandler.Validate(command, null).Errors));
        Assert.Equal((nameof(SavePlayer.Name), "Enter a name."), (error.Field, error.Message));
    }

    [Fact]
    public void The_players_follow_their_events()
    {
        PathMapping[] pathMappings = [new(new PlayerPath("/storage"), new LocalPath("/mnt/storage"))];

        var players = Stored
            .Apply(new PlayerRenamed(TheaterId, "Lounge"))
            .Apply(new PlayerChanged(TheaterId, false, KodiEndpoint.Create("lounge.lan", 9090, 60, 10).Value, pathMappings))
            .Apply(new PlayerRemoved(BedroomId));

        var player = Assert.Single(players.All);
        Assert.Equal((TheaterId, "Lounge", false, "lounge.lan"), (player.Id, player.Name, player.Enabled, ((KodiEndpoint)player.Endpoint).Host));
        Assert.Equal(pathMappings, player.PathMappings);
        Assert.Equal([BedroomId], players.RemovedIds);
    }

    private static IEnumerable<(string Field, string Message)> FieldErrors(Result result) =>
        result.Errors.Cast<FieldError>().Select(error => (error.Field, error.Message));

    /// <summary>An endpoint of a player type other than Kodi.</summary>
    private sealed record OtherEndpoint : PlayerEndpoint
    {
        public override PlayerPath ToPlayerPath(string path) => new(path);

        public override Result Validate() => Result.Ok();
    }
}
