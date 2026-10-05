using System.Reactive.Linq;
using Debarr.EventStore;
using Debarr.Extensions;
using Debarr.Playing;
using Fisher;
using FluentResults;
using Microsoft.AspNetCore.Components;
using Wolverine.Runtime;

namespace Debarr.Components.Pages.Settings;

public partial class KodiPlayerModal(IDocumentStore store, IWolverineRuntime runtime, PlayerConnectionFactory playerConnectionFactory, PlayerConnectionService playerConnectionService)
{
    private readonly EditedForm<KodiPlayerForm> _form = new(form => form.Copy(), (edited, saved) => edited.HasChangesFrom(saved));

    /// <summary>The player as saved when the modal opened, or a new player's defaults.</summary>
    [Parameter]
    public KodiPlayerForm Saved { get; set; } = default!;

    private KodiPlayerForm Form => _form.Model!;

    /// <summary>The saved player's connection state as the modal opened; null for a new or disabled player.</summary>
    private PlayerConnectionState? ConnectionState { get; set; }

    public override IReadOnlyCollection<string> AdvancedFields { get; } =
        [nameof(KodiPlayerForm.PingIntervalSeconds), nameof(KodiPlayerForm.RequestTimeoutSeconds)];

    protected override string RemoveTitle => "Remove Player";

    protected override string RemoveMessage => $"Remove the player {Form.Name}? Its connection closes and its path mappings go with it.";

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _form.Take(Saved);
    }

    protected override async Task OnInitializedAsync()
    {
        if (!Form.IsNew && Form.Enabled)
        {
            var connectionStates = await playerConnectionService.ConnectionStates.FirstAsync();
            ConnectionState = connectionStates.GetValueOrDefault(Form.Id)?.State;
        }
    }

    private void AddPathMapping() => Form.PathMappings.Add(new PathMappingForm());

    private void RemovePathMapping(PathMappingForm pathMapping)
    {
        Form.PathMappings.Remove(pathMapping);
        ModalAction.ClearRowFieldErrors(nameof(SavePlayer.PathMappings));
    }

    private string PathMappingField(PathMappingForm pathMapping, string path) =>
        SavePlayer.PathMappingField(Form.PathMappings.IndexOf(pathMapping), path);

    protected override async Task<Result> ValidateIntegrationAsync(CancellationToken cancellationToken)
    {
        var players = await ReadPlayersAsync(cancellationToken);
        return Form.ToSavePlayer(players).Bind(command => SavePlayerHandler.Validate(command, players));
    }

    /// <summary>Connects once with the unsaved values, and returns the player's version or the error.</summary>
    protected override async Task<(bool Succeeded, string? Message)> TestIntegrationAsync(CancellationToken cancellationToken)
    {
        var command = Form.ToSavePlayer(Players.Empty).Value;
        var player = new Player(command.PlayerId, command.Name, command.Enabled, command.Endpoint, []);
        var test = await playerConnectionFactory.TestAsync(player, cancellationToken);
        return test.IsSuccess ? (true, $"Connected to {test.Value}") : (false, test.GetFormError());
    }

    protected override async Task<ResultBase> SaveIntegrationAsync(CancellationToken cancellationToken)
    {
        var players = await ReadPlayersAsync(cancellationToken);
        if (players.Find(Form.Id) is { Endpoint: KodiEndpoint kodi } player)
        {
            _form.Take(KodiPlayerForm.FromKodiPlayer(player, kodi));
        }

        return await _form.SaveAsync(async form =>
        {
            var command = form.ToSavePlayer(players);
            return command.IsSuccess ? await runtime.SendCommandAsync(command.Value, cancellationToken) : command.ToResult();
        });
    }

    private async Task<Players> ReadPlayersAsync(CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        return await Players.ReadAsync(session, cancellationToken);
    }

    protected override async Task<ResultBase> RemoveIntegrationAsync(CancellationToken cancellationToken) =>
        await runtime.SendCommandAsync(new RemovePlayer(Form.Id), cancellationToken);
}
