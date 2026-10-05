using System.Globalization;
using System.Net.Sockets;
using System.Net;
using Bunit;
using Debarr.Components.Pages.Settings;
using Debarr.EventStore;
using Debarr.Playing;
using Debarr.Tests.Extensions;
using Fisher;
using Wolverine.Runtime;

namespace Debarr.Tests.Components.Pages.Settings;

public sealed class PlayersPageTests : PageTestContext
{
    [Fact]
    public void An_empty_page_says_what_a_player_is()
    {
        var cut = RenderPage<PlayersPage>();

        cut.WaitForAssertion(() => Assert.Contains("A player is a Kodi that Debarr connects to.", cut.Find("#players-empty").TextContent), Timeout);
    }

    [Fact]
    public async Task The_page_reloads_when_a_commit_changes_a_player()
    {
        var cut = RenderPage<PlayersPage>();
        cut.WaitForElement("#players-empty", Timeout);

        await using (var session = GetAppService<IDocumentStore>().LightweightSession())
        {
            await session.Events.AppendAtCurrentVersionAsync(Players.StreamId, new PlayerAdded(Guid.NewGuid(), "Bedroom", false, KodiEndpoint.Create("kodi.local", 9090, 60, 10).Value, []));
            await session.SaveChangesAsync(CancellationToken);
        }

        cut.WaitForAssertion(() => Assert.Contains("Bedroom", cut.Find(".player-card").TextContent), Timeout);
        Assert.All(cut.FindAll(".player-card, #players-add"), card => Assert.Equal("BUTTON", card.TagName));
    }

    [Fact]
    public async Task A_disconnected_player_shows_its_error_on_its_card_and_as_an_error_alert_in_its_modal()
    {
        var saved = await GetAppService<IWolverineRuntime>().SendCommandAsync(
            new SavePlayer(Guid.NewGuid(), "Bedroom", true, KodiEndpoint.Create("127.0.0.1", ClosedPort(), 60, 10).Value, []),
            CancellationToken);
        Assert.True(saved.IsSuccess);

        var cut = RenderPage<PlayersPage>();

        cut.WaitForAssertion(() => Assert.StartsWith("Disconnected: ", cut.Find(".player-card-state").TextContent.Trim()), Timeout);
        await cut.RaiseClickAsync(".player-card", Timeout);
        cut.WaitForAssertion(() => Assert.StartsWith("Disconnected: ", cut.Find("#kodi-connection").TextContent.Trim()), Timeout);
        var alert = cut.Find("#kodi-connection");
        Assert.Contains("mud-alert-text-error", alert.ClassList);
        Assert.Empty(alert.QuerySelectorAll(".mud-alert-icon"));
        Assert.Single(alert.QuerySelectorAll(".state-label .mud-icon-root"));
    }

    [Fact]
    public async Task The_modal_has_a_close_button_in_its_header_and_enable_on_its_own_first_row()
    {
        var cut = RenderPage<PlayersPage>();
        await cut.RaiseClickAsync("#players-add", Timeout);
        cut.WaitForElement("#kodi-name", Timeout);

        Assert.NotEmpty(cut.FindAll(".mud-dialog-title .mud-button-close"));
        Assert.NotNull(cut.Find(".mud-dialog .mud-grid-item").QuerySelector(".switch-field #kodi-enabled"));
    }

    [Fact]
    public async Task Show_advanced_in_the_modal_reveals_the_ping_interval_and_request_timeout()
    {
        var cut = RenderPage<PlayersPage>();
        await cut.RaiseClickAsync("#players-add", Timeout);
        cut.WaitForElement("#kodi-name", Timeout);
        Assert.Empty(cut.FindAll("#kodi-ping-interval"));

        await cut.RaiseClickAsync(".show-advanced", Timeout);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("#kodi-ping-interval")), Timeout);
        Assert.Single(cut.FindAll("#kodi-request-timeout"));
    }

    [Fact]
    public async Task A_refused_name_shows_beneath_the_name_field_keeps_the_edits_and_clears_once_the_name_changes()
    {
        var saved = await GetAppService<IWolverineRuntime>().SendCommandAsync(
            new SavePlayer(Guid.NewGuid(), "Theater", false, KodiEndpoint.Create("127.0.0.1", ClosedPort(), 60, 10).Value, []),
            CancellationToken);
        Assert.True(saved.IsSuccess);
        var cut = RenderPage<PlayersPage>();
        await cut.RaiseClickAsync("#players-add", Timeout);
        await cut.RaiseInputAsync("#kodi-name", "Theater", Timeout);
        await cut.RaiseInputAsync("#kodi-host", "192.168.1.20", Timeout);

        await cut.RaiseClickAsync("#kodi-save", Timeout);

        cut.WaitForAssertion(() => Assert.Contains("A player named Theater already exists.", NameField(cut).TextContent), Timeout);
        Assert.Empty(cut.FindAll("#kodi-error"));
        Assert.Equal("192.168.1.20", cut.Find("#kodi-host").GetAttribute("value"));

        await cut.RaiseInputAsync("#kodi-name", "Bedroom", Timeout);

        cut.WaitForAssertion(() => Assert.DoesNotContain("already exists", NameField(cut).TextContent), Timeout);
    }

    [Theory]
    [InlineData("#kodi-save")]
    [InlineData("#kodi-test")]
    public async Task A_name_another_player_has_is_refused_together_with_a_refused_host(string button)
    {
        await SaveTheaterAsync();
        var cut = RenderPage<PlayersPage>();
        await cut.RaiseClickAsync("#players-add", Timeout);
        await cut.RaiseInputAsync("#kodi-name", "Theater", Timeout);

        await cut.RaiseClickAsync(button, Timeout);

        cut.WaitForAssertion(() => Assert.Contains("Enter the host.", cut.Find("#kodi-host").Closest(".mud-input-control")!.TextContent), Timeout);
        Assert.Contains("A player named Theater already exists.", NameField(cut).TextContent);
        Assert.Empty(cut.FindAll("#kodi-error"));
        Assert.Empty(cut.FindAll("#kodi-test-result"));
    }

    [Fact]
    public async Task Test_refuses_a_name_another_player_has_beneath_the_name_field()
    {
        await SaveTheaterAsync();
        var cut = RenderPage<PlayersPage>();
        await cut.RaiseClickAsync("#players-add", Timeout);
        await cut.RaiseInputAsync("#kodi-name", "Theater", Timeout);
        await cut.RaiseInputAsync("#kodi-host", "127.0.0.1", Timeout);
        await cut.RaiseInputAsync("#kodi-port", ClosedPort().ToString(CultureInfo.InvariantCulture), Timeout);

        await cut.RaiseClickAsync("#kodi-test", Timeout);

        cut.WaitForAssertion(() => Assert.Contains("A player named Theater already exists.", NameField(cut).TextContent), Timeout);
        Assert.Empty(cut.FindAll("#kodi-test-result"));
    }

    [Fact]
    public async Task Save_refuses_a_blank_name_a_blank_host_and_each_refused_path_mapping_together_beneath_their_fields()
    {
        var cut = RenderPage<PlayersPage>();
        await cut.RaiseClickAsync("#players-add", Timeout);
        cut.WaitForElement("#kodi-name", Timeout);
        await cut.RaiseClickAsync("#kodi-add-path-mapping", Timeout);
        await cut.RaiseClickAsync("#kodi-add-path-mapping", Timeout);
        await cut.RaiseClickAsync("#kodi-add-path-mapping", Timeout);
        await Inputs(cut, "Player Path")[0].InputAsync("smb://nas/media");
        await Inputs(cut, "Player Path")[1].InputAsync("smb://nas//media/");
        await Inputs(cut, "Local Path")[0].InputAsync("/media");
        await Inputs(cut, "Local Path")[1].InputAsync("/mnt/media");

        await cut.RaiseClickAsync("#kodi-save", Timeout);

        cut.WaitForAssertion(() => Assert.Contains("Enter the host.", cut.Find("#kodi-host").Closest(".mud-input-control")!.TextContent), Timeout);
        Assert.Contains("Enter a name.", cut.Find("#kodi-name").Closest(".mud-input-control")!.TextContent);
        Assert.Equal(2, cut.FindAll(".mud-dialog .mud-input-control").Count(control => control.TextContent.Contains("smb://nas/media has another path mapping.")));
        Assert.Contains("Enter the player path.", Inputs(cut, "Player Path")[2].Closest(".mud-input-control")!.TextContent);
        Assert.Contains("Enter the local path.", Inputs(cut, "Local Path")[2].Closest(".mud-input-control")!.TextContent);
        Assert.Empty(cut.FindAll("#kodi-error"));
    }

    [Fact]
    public async Task Save_refuses_a_port_out_of_range_beneath_the_port_field_as_entered()
    {
        var cut = RenderPage<PlayersPage>();
        await cut.RaiseClickAsync("#players-add", Timeout);
        cut.WaitForElement("#kodi-name", Timeout);
        await cut.RaiseInputAsync("#kodi-name", "Theater", Timeout);
        await cut.RaiseInputAsync("#kodi-host", "192.168.1.20", Timeout);
        await cut.RaiseInputAsync("#kodi-port", "0", Timeout);
        cut.WaitForAssertion(() => Assert.Equal("0", cut.Find("#kodi-port").GetAttribute("value")), Timeout);

        await cut.RaiseClickAsync("#kodi-save", Timeout);

        cut.WaitForAssertion(() => Assert.Contains("Enter 1 to 65535.", cut.Find("#kodi-port").Closest(".mud-input-control")!.TextContent), Timeout);
        Assert.Equal("0", cut.Find("#kodi-port").GetAttribute("value"));
    }

    [Fact]
    public async Task A_refused_ping_interval_behind_show_advanced_turns_show_advanced_on_with_its_error()
    {
        var cut = RenderPage<PlayersPage>();
        await cut.RaiseClickAsync("#players-add", Timeout);
        cut.WaitForElement("#kodi-name", Timeout);
        await cut.RaiseInputAsync("#kodi-name", "Theater", Timeout);
        await cut.RaiseInputAsync("#kodi-host", "192.168.1.20", Timeout);
        await cut.RaiseClickAsync(".mud-dialog .show-advanced", Timeout);
        cut.WaitForElement("#kodi-ping-interval", Timeout);
        await cut.RaiseInputAsync("#kodi-ping-interval", "0", Timeout);
        cut.WaitForAssertion(() => Assert.Equal("0", cut.Find("#kodi-ping-interval").GetAttribute("value")), Timeout);
        await cut.RaiseClickAsync(".mud-dialog .show-advanced", Timeout);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#kodi-ping-interval")), Timeout);

        await cut.RaiseClickAsync("#kodi-test", Timeout);

        cut.WaitForAssertion(() => Assert.Contains("Enter 1 or more.", cut.Find("#kodi-ping-interval").Closest(".mud-input-control")!.TextContent), Timeout);
        Assert.Empty(cut.FindAll("#kodi-test-result"));
    }

    [Fact]
    public async Task A_save_from_a_modal_whose_player_another_tab_saved_is_refused_and_both_saves_are_kept()
    {
        await SaveTheaterAsync();
        var first = RenderPage<PlayersPage>();
        var second = RenderPage<PlayersPage>(tab: OpenAnotherTab());
        await first.RaiseClickAsync(".player-card", Timeout);
        await first.RaiseInputAsync("#kodi-name", "Lounge", Timeout);
        await second.RaiseClickAsync(".player-card", Timeout);
        await second.RaiseInputAsync("#kodi-name", "Bedroom", Timeout);
        await second.RaiseClickAsync("#kodi-save", Timeout);
        await Poll.UntilAsync(async () => (await ReadPlayersAsync()).All.Single().Name == "Bedroom", Timeout);

        await first.RaiseClickAsync("#kodi-save", Timeout);

        first.WaitForAssertion(() => Assert.Equal("Saved in another tab. Reload to see the change.", first.Find("#kodi-error").TextContent.Trim()), Timeout);
        Assert.Equal("Lounge", first.Find("#kodi-name").GetAttribute("value"));
        Assert.Equal("Bedroom", (await ReadPlayersAsync()).All.Single().Name);
    }

    [Fact]
    public async Task Save_adds_a_new_player_and_closes_the_modal()
    {
        var cut = RenderPage<PlayersPage>();
        await cut.RaiseClickAsync("#players-add", Timeout);
        await cut.RaiseInputAsync("#kodi-name", "Theater", Timeout);
        await cut.RaiseInputAsync("#kodi-host", "192.168.1.20", Timeout);

        await cut.RaiseClickAsync("#kodi-save", Timeout);

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#kodi-name")), Timeout);
        var player = Assert.Single((await ReadPlayersAsync()).All);
        Assert.Equal("Theater", player.Name);
        Assert.Equal("192.168.1.20", Assert.IsType<KodiEndpoint>(player.Endpoint).Host);
    }

    private async Task<Players> ReadPlayersAsync()
    {
        await using var session = GetAppService<IDocumentStore>().QuerySession();
        return await Players.ReadAsync(session, CancellationToken);
    }

    private async Task SaveTheaterAsync() =>
        Assert.True((await GetAppService<IWolverineRuntime>().SendCommandAsync(
            new SavePlayer(Guid.NewGuid(), "Theater", false, KodiEndpoint.Create("127.0.0.1", ClosedPort(), 60, 10).Value, []),
            CancellationToken)).IsSuccess);

    /// <summary>The Name field's control, which holds its helper and error text.</summary>
    private static AngleSharp.Dom.IElement NameField(IRenderedComponent<Debarr.Components.Layout.UISettingsProvider> cut) =>
        cut.Find("#kodi-name").Closest(".mud-input-control")!;

    private static List<AngleSharp.Dom.IElement> Inputs(IRenderedComponent<Debarr.Components.Layout.UISettingsProvider> cut, string label) =>
        [.. cut.FindAll(".mud-dialog input").Where(input => input.Closest(".mud-input-control")!.TextContent.Contains(label))];

    /// <summary>A loopback port that nothing listens on once the method returns.</summary>
    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
