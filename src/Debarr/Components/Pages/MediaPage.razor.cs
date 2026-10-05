using Debarr.Activity;
using Debarr.Detecting;
using Fisher;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Debarr.Components.Pages;

public partial class MediaPage(IDocumentStore store, DetectionOrchestrator detectionOrchestrator, NavigationManager navigation)
{
    private const string AllFilter = "all";

    private static readonly StatusFilter[] Filters =
    [
        new(AllFilter, null),
        new("detected", VideoFileStatus.Detected),
        new("from-file", VideoFileStatus.FromFile),
        new("manual", VideoFileStatus.Manual),
        new("pending", VideoFileStatus.Pending),
        new("failed", VideoFileStatus.Failed),
    ];

    private static readonly SortOption[] SortOptions =
    [
        new(SortLabels.Path, "Path", "A to Z", "Z to A"),
        new(SortLabels.AspectRatio, "Ratio", "Narrowest First", "Widest First"),
        new(SortLabels.Confidence, "Confidence", "Lowest First", "Highest First"),
        new(SortLabels.Status, "Status", "Pending First", "Manual First"),
        new(SortLabels.FirstSeen, "First Seen", "Oldest First", "Newest First"),
    ];

    private static readonly TableView DefaultView = new(SortLabels.Path, false, 0, TableView.DefaultPageSize);

    private const string DetectNowBusy = "Available when the Detect Now running on another file ends.";

    private const string ConfidenceExplanation =
        "The share of samples that agree on the picture's size. A ratio from the file is 90%.";

    // The video file count for each status among the rows the search matches; null until the first reload.
    private Dictionary<VideoFileStatus, int>? _counts;
    private bool _libraryIsEmpty;
    private StandardRatios _standardRatios = StandardRatios.Default;
    private IReadOnlyList<RunningDetection> _running = [];
    private readonly PageAction _detectNow = new();

    private readonly PagedTableView<MediaRow> _view = new(navigation, SortOptions, DefaultView);

    /// <summary>The status filter's value, such as failed; every status when absent.</summary>
    [SupplyParameterFromQuery(Name = "status")]
    private string? StatusQuery { get; set; }

    /// <summary>The text a path contains; every video file when absent.</summary>
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

    private string? Search => string.IsNullOrWhiteSpace(SearchQuery) ? null : SearchQuery;

    private Dictionary<string, string?> UrlFilters => new()
    {
        ["status"] = SelectedFilter.Status is null ? null : SelectedFilter.Value,
        ["search"] = Search,
    };

    private StatusFilter SelectedFilter =>
        Filters.FirstOrDefault(filter => string.Equals(filter.Value, StatusQuery, StringComparison.OrdinalIgnoreCase)) ?? Filters[0];

    private bool DetectNowRunning => _running.Any(running => running.Origin == DetectionOrigin.DetectNow);

    protected override IReadOnlySet<string> ReadModels { get; } = new HashSet<string> { nameof(DetectionSettings), MediaRowProjection.ReadModel };

    protected override bool ShowsActivity(ActivityEvent activityEvent) => DetectionOrchestrator.ChangesRunningDetections(activityEvent);

    /// <summary>Reads the status counts, the standard ratios and the running detections, then the page of rows on screen.</summary>
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
        await using (var session = store.QuerySession())
        {
            _standardRatios = (await DetectionSettings.ReadAsync(session, cancellationToken)).StandardRatios;
            _libraryIsEmpty = !await session.AnyListedMediaRowAsync(cancellationToken);
            _counts = await session.CountMediaRowsByStatusAsync(Search, cancellationToken);
        }

        _running = detectionOrchestrator.RunningDetections;
    }

    /// <summary>Reads one page of rows, filtered, searched and sorted in the database.</summary>
    private async Task<TableData<MediaRow>> ReadPageAsync(TableState state, CancellationToken cancellationToken)
    {
        // The page holds the sort, so a link and the phone's sort control set it as a header click does.
        await using var session = store.QuerySession();
        var (totalCount, rows) = await session.ReadMediaPageAsync(
            new MediaRowView(SelectedFilter.Status, Search, SortOf(_view.Current.Sort), _view.Current.Descending), state.Page, state.PageSize, cancellationToken);
        return new TableData<MediaRow> { TotalItems = totalCount, Items = rows };
    }

    private int CountOf(StatusFilter filter) =>
        filter.Status is { } status ? _counts?.GetValueOrDefault(status) ?? 0 : _counts?.Values.Sum() ?? 0;

    private SnappedAspectRatio Snap(double rawAspectRatio) => _standardRatios.Snap(new AspectRatio(rawAspectRatio));

    /// <summary>Whether the Ratio column has something to show: Don't Send, an override's ratio or a detection result.</summary>
    private static bool HasAspectRatio(MediaRow row) =>
        row.Override is { DontSend: true } or { AspectRatio: not null } || row.CurrentResult is not null;

    private RunningDetection? RunningDetectionOf(MediaRow row) => _running.FirstOrDefault(running => running.VideoFile == row.FileHash);

    private string DetectNowTooltip(RunningDetection? running) =>
        running is not null ? "Detecting" : DetectNowRunning ? DetectNowBusy : "Detect Now";

    private string DetectNowLabel(RunningDetection? running) =>
        running is not null ? "Detecting" : DetectNowRunning ? $"Detect Now: {DetectNowBusy}" : "Detect Now";

    private static MediaRowSort SortOf(string sortLabel) => sortLabel switch
    {
        SortLabels.AspectRatio => MediaRowSort.AspectRatio,
        SortLabels.Confidence => MediaRowSort.Confidence,
        SortLabels.Status => MediaRowSort.Status,
        SortLabels.FirstSeen => MediaRowSort.FirstSeen,
        _ => MediaRowSort.Path,
    };

    private static string FileName(FilePath filePath) => Path.GetFileName(filePath.Path.Value);

    private static string? Folder(FilePath filePath) => Path.GetDirectoryName(filePath.Path.Value);

    /// <summary>A new filter or search shows its first page.</summary>
    private void SelectFilter(string? value) => _view.Filter("status", value is null or AllFilter ? null : value);

    private void SearchFor(string? search) => _view.Filter("search", string.IsNullOrWhiteSpace(search) ? null : search);

    private async Task DetectNowAsync(MediaRow row)
    {
        if (RunningDetectionOf(row) is not null)
        {
            return;
        }

        await _detectNow.RunAsync(() => detectionOrchestrator.DetectNowAsync(row.FileHash, CancellationToken.None), Logger, "Detect Now did not start.");

        _running = detectionOrchestrator.RunningDetections;
    }

    /// <summary>A status filter: its query string value and the status it shows, or null for every status.</summary>
    private sealed record StatusFilter(string Value, VideoFileStatus? Status)
    {
        public string Label => Status is { } status ? VideoFileStatusText.DisplayOf(status).Text : "All";
    }

    private static class SortLabels
    {
        public const string Path = "path";
        public const string AspectRatio = "ratio";
        public const string Confidence = "confidence";
        public const string Status = "status";
        public const string FirstSeen = "first-seen";
    }
}
