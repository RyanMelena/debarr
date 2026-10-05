using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Debarr.Activity;
using Debarr.Extensions;
using Debarr.Playing;
using Fisher;
using MudBlazor;

namespace Debarr.Components.Pages.Settings;

public partial class PlayersPage(IDocumentStore store, IDialogService dialogService, PlayerConnectionService playerConnectionService)
{
    private static readonly DialogOptions ModalOptions = new() { MaxWidth = MaxWidth.Small, FullWidth = true, BackdropClick = false };

    private List<Player>? _players;
    private IReadOnlyDictionary<Guid, PlayerConnectionStateChangedEvent> _connectionStates = new Dictionary<Guid, PlayerConnectionStateChangedEvent>();

    protected override IReadOnlySet<string> ReadModels { get; } = new HashSet<string> { nameof(Players) };

    protected override bool ShowsActivity(ActivityEvent activityEvent) => activityEvent is PlayerConnectionStateChangedEvent;

    protected override async Task ReloadAsync(CancellationToken cancellationToken)
    {
        _connectionStates = await playerConnectionService.ConnectionStates.FirstAsync().ToTask(cancellationToken);
        await using var session = store.QuerySession();
        _players = [.. (await Players.ReadAsync(session, cancellationToken)).All.OrderBy(player => player.Name, StringComparer.Ordinal)];
    }

    private static string PathMappingsText(Player player) => player.PathMappings.Count switch
    {
        0 => "No path mappings",
        1 => "1 path mapping",
        var count => $"{count} path mappings",
    };

    /// <summary>The state of an enabled player's connection; null while the player is disabled, and until its connection enters a state.</summary>
    private PlayerConnectionState? StateOf(Player player) =>
        player.Enabled ? _connectionStates.GetValueOrDefault(player.Id)?.State : null;

    private Task AddAsync() => ShowKodiAsync("Add Player - Kodi", KodiPlayerForm.ForNewPlayer());

    private Task EditAsync(Player player) => player.Endpoint switch
    {
        KodiEndpoint kodi => ShowKodiAsync("Edit Player - Kodi", KodiPlayerForm.FromKodiPlayer(player, kodi)),
        _ => Task.CompletedTask,
    };

    private Task ShowKodiAsync(string title, KodiPlayerForm form) =>
        dialogService.ShowAsync<KodiPlayerModal>(
            title,
            new DialogParameters<KodiPlayerModal> { { modal => modal.Saved, form } },
            ModalOptions);
}
