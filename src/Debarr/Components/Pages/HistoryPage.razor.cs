using Debarr.EventStore;
using Debarr.Extensions;
using Debarr.Playing;
using Fisher;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wolverine.Runtime;

namespace Debarr.Components.Pages;

public partial class HistoryPage(IDocumentStore store, IWolverineRuntime runtime, IDialogService dialogService, NavigationManager navigation)
{
    private const string AllFilter = "all";

    private static readonly OutcomeFilter[] Filters =
    [
        new(AllFilter, "All", null),
        new("sent", "Sent", PlaybackRowFilter.Sent),
        new("not-sent", "Not Sent", PlaybackRowFilter.NotSent),
        new("delivery-failed", "Delivery Failed", PlaybackRowFilter.DeliveryFailed),
    ];

    private static readonly SortOption[] SortOptions =
    [
        new(SortLabels.Time, "Time", "Oldest First", "Newest First"),
        new(SortLabels.Player, "Player", "A to Z", "Z to A"),
        new(SortLabels.Title, "Title", "A to Z", "Z to A"),
        new(SortLabels.Path, "Path", "A to Z", "Z to A"),
    ];

    private static readonly TableView DefaultView = new(SortLabels.Time, true, 0, TableView.DefaultPageSize);

    // Every playback, whatever the filters; null until the first reload.
    private int? _totalCount;

    // The playback count for each filter among the playbacks the player and search match.
    private PlaybackRowCounts _counts = new(0, 0, 0, 0);
    private List<string> _playerNames = [];
    private readonly PageAction _clearHistory = new();

    private readonly PagedTableView<PlaybackRow> _view = new(navigation, SortOptions, DefaultView);

    /// <summary>The outcome filter's value, such as not-sent; every playback when absent.</summary>
    [SupplyParameterFromQuery(Name = "outcome")]
    private string? OutcomeQuery { get; set; }

    /// <summary>The player's name at playback; every player when absent.</summary>
    [SupplyParameterFromQuery(Name = "player")]
    private string? PlayerQuery { get; set; }

    /// <summary>The text the title or a path contains; every playback when absent.</summary>
    [SupplyParameterFromQuery(Name = "search")]
    private string? SearchQuery { get; set; }

    [SupplyParameterFromQuery(Name = "sort")]
    private string? SortQuery { get; set; }

    /// <summary>asc or desc.</summary>
    [SupplyParameterFromQuery(Name = "dir")]
    private string? DirectionQuery { get; set; }

    /// <summary>The page, counted from 1.</summary>
    [SupplyParameterFromQuery(Name = "page")]
    private int? PageQuery { get; set; }

    [SupplyParameterFromQuery(Name = "size")]
    private int? PageSizeQuery { get; set; }

    private OutcomeFilter SelectedFilter =>
        Filters.FirstOrDefault(filter => string.Equals(filter.Value, OutcomeQuery, StringComparison.OrdinalIgnoreCase)) ?? Filters[0];

    private string? Search => string.IsNullOrWhiteSpace(SearchQuery) ? null : SearchQuery;

    private string? Player => string.IsNullOrEmpty(PlayerQuery) ? null : PlayerQuery;

    private Dictionary<string, string?> UrlFilters => new()
    {
        ["outcome"] = SelectedFilter.Value == AllFilter ? null : SelectedFilter.Value,
        ["player"] = Player,
        ["search"] = Search,
    };

    private PlaybackRowView View => new(
        SelectedFilter.Outcome,
        Player,
        Search,
        _view.Current.Sort switch
        {
            SortLabels.Player => PlaybackRowSort.Player,
            SortLabels.Title => PlaybackRowSort.Title,
            SortLabels.Path => PlaybackRowSort.Path,
            _ => PlaybackRowSort.Time,
        },
        _view.Current.Descending);

    protected override IReadOnlySet<string> ReadModels { get; } = new HashSet<string> { PlaybackRowProjection.ReadModel, HistoryClearProjection.ReadModel };

    /// <summary>Reads the counts and the player names, then the page of playbacks on screen.</summary>
    protected override async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await ReadCountsAsync(cancellationToken);
        await _view.ReloadAsync();
    }

    /// <summary>The first reload reads the view the URL names.</summary>
    protected override async Task OnInitializedAsync()
    {
        _view.TakeFromUrl(UrlFilters, SortQuery, DirectionQuery, PageQuery, PageSizeQuery);
        await base.OnInitializedAsync();
    }

    /// <summary>Shows the view a link or the back button puts in the URL while the page is open.</summary>
    protected override Task OnParametersSetAsync() =>
        _view.ShowFromUrlAsync(UrlFilters, SortQuery, DirectionQuery, PageQuery, PageSizeQuery, () => ReadCountsAsync(CancellationToken.None));

    private async Task ReadCountsAsync(CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        _totalCount = await session.CountShownPlaybackRowsAsync(cancellationToken);
        _playerNames = [.. await session.ReadPlayerNamesAsync(cancellationToken)];

        _counts = await session.CountPlaybackRowsByOutcomeAsync(View.Scope, cancellationToken);
    }

    /// <summary>Reads one page of playbacks, filtered, searched and sorted in the database, or the last page when fewer playbacks match than the page on screen needs.</summary>
    private async Task<TableData<PlaybackRow>> ReadPageAsync(TableState state, CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        var rows = await session.ReadHistoryPageAsync(View, _counts, state.Page, state.PageSize, cancellationToken);
        return new TableData<PlaybackRow> { TotalItems = _counts[View.Outcome], Items = rows };
    }

    private int CountOf(OutcomeFilter filter) => _counts[filter.Outcome];

    /// <summary>A new filter, player or search shows its first page.</summary>
    private void SelectFilter(string? value) => _view.Filter("outcome", value is null or AllFilter ? null : value);

    private void SelectPlayer(string? player) => _view.Filter("player", player);

    private void SearchFor(string? search) => _view.Filter("search", string.IsNullOrWhiteSpace(search) ? null : search);

    private async Task ClearHistoryAsync()
    {
        var count = _totalCount ?? 0;
        var confirmed = await dialogService.ConfirmAsync(
            "Clear History",
            $"Clear {(count == 1 ? "the one playback" : $"all {count.ToCountText()} playbacks")} and their deliveries from the history?",
            "Clear History");
        if (!confirmed)
        {
            return;
        }

        await _clearHistory.RunAsync(() => runtime.SendCommandAsync(new ClearHistory(), CancellationToken.None), Logger, "The history was not cleared.");
    }

    private sealed record OutcomeFilter(string Value, string Label, PlaybackRowFilter? Outcome);

    private static class SortLabels
    {
        public const string Time = "time";
        public const string Player = "player";
        public const string Title = "title";
        public const string Path = "path";
    }
}
