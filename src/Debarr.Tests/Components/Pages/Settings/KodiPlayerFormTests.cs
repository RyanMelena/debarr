using Debarr.Components.Pages.Settings;
using Debarr.Playing;

namespace Debarr.Tests.Components.Pages.Settings;

public class KodiPlayerFormTests
{
    private static readonly KodiEndpoint Endpoint = KodiEndpoint.Create("kodi.lan", 9090, 60, 10).Value;

    private static readonly Guid PlayerId = Guid.NewGuid();

    [Fact]
    public void A_save_trims_the_values_and_canonicalises_each_player_path()
    {
        var form = new KodiPlayerForm
        {
            Id = PlayerId,
            Name = " Theater ",
            Enabled = true,
            Host = " kodi.lan ",
            Port = 9090,
            PingIntervalSeconds = 60,
            RequestTimeoutSeconds = 10,
            PathMappings =
            [
                new PathMappingForm { PlayerPath = " smb://nas/My%20Media//movies/ ", LocalPath = " /media/movies " },
                new PathMappingForm { PlayerPath = @"D:\Media\TV\", LocalPath = "/media/tv" },
            ],
        };

        var command = form.ToSavePlayer(Players.Empty).Value;
        var added = Assert.IsType<PlayerAdded>(Assert.Single(SavePlayerHandler.Handle(command, null)));

        Assert.Equal((PlayerId, "Theater", true), (command.PlayerId, command.Name, command.Enabled));
        Assert.Equal(Endpoint, added.Endpoint);
        Assert.Equal(
            [("smb://nas/My Media/movies", "/media/movies"), ("D:/Media/TV", "/media/tv")],
            added.PathMappings.Select(pathMapping => (pathMapping.PlayerPath.Value, pathMapping.LocalPath.Value)));
    }

    [Fact]
    public void A_new_player_takes_the_kodi_endpoint_defaults_and_an_id_of_its_own()
    {
        var form = KodiPlayerForm.ForNewPlayer();

        Assert.Equal((true, "", true, "", 9090, 60, 10), (form.IsNew, form.Name, form.Enabled, form.Host, form.Port, form.PingIntervalSeconds, form.RequestTimeoutSeconds));
        Assert.NotEqual(Guid.Empty, form.Id);
        Assert.Empty(form.PathMappings);
    }

    [Fact]
    public void From_kodi_player_keeps_a_saved_players_id()
    {
        var form = KodiPlayerForm.FromKodiPlayer(new Player(PlayerId, "Theater", true, Endpoint, []), Endpoint);

        Assert.Equal((PlayerId, false), (form.Id, form.IsNew));
        Assert.Equal(PlayerId, form.ToSavePlayer(Players.Empty).Value.PlayerId);
    }

    [Fact]
    public void A_save_refuses_each_endpoint_value_outside_its_bounds_beneath_its_field()
    {
        var form = new KodiPlayerForm { Name = "Theater", Host = " ", Port = 0, PingIntervalSeconds = 0, RequestTimeoutSeconds = 0 };

        Assert.Equal(
            [
                (nameof(KodiPlayerForm.Host), "Enter the host."),
                (nameof(KodiPlayerForm.Port), "Enter 1 to 65535."),
                (nameof(KodiPlayerForm.PingIntervalSeconds), "Enter 1 or more."),
                (nameof(KodiPlayerForm.RequestTimeoutSeconds), "Enter 1 or more."),
            ],
            form.ToSavePlayer(Players.Empty).Errors.Cast<FieldError>().Select(error => (error.Field, error.Message)));
    }

    [Fact]
    public void A_copy_edits_its_path_mappings_apart_and_an_edited_path_mapping_is_a_change()
    {
        var saved = new KodiPlayerForm { PathMappings = [new PathMappingForm { PlayerPath = "smb://nas/media", LocalPath = "/media" }] };
        var edited = saved.Copy();

        edited.PathMappings[0].LocalPath = "/mnt/media";

        Assert.Equal("/media", saved.PathMappings[0].LocalPath);
        Assert.True(edited.HasChangesFrom(saved));
        Assert.False(saved.Copy().HasChangesFrom(saved));
    }
}
