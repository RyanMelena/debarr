using Debarr.Activity;
using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Extensions;
using Debarr.Playing;
using Debarr.Scanning;
using Fisher;
using FluentResults;
using Microsoft.AspNetCore.Components;
using Wolverine.Runtime;

namespace Debarr.Components.Pages;

public partial class VideoFileDetailPage(IDocumentStore store, DetectionOrchestrator detectionOrchestrator, IWolverineRuntime runtime, NavigationManager navigation)
{
    private bool _loaded;

    // Null for an address that names no video file.
    private VideoFile? _videoFile;

    private IReadOnlyList<CropSample> _samples = [];
    private HashSet<CropSample> _agreeingSamples = [];
    private List<string> _rootPaths = [];
    private List<Detection> _detections = [];
    private bool _detectionsShowPaths;
    private IReadOnlyList<PlaybackRow> _playbacks = [];
    private bool _playbacksShowPaths;
    private StandardRatios _standardRatios = StandardRatios.Default;
    private readonly EditedForm<OverrideForm> _override = new(model => model.Normalized(), (edited, saved) => edited.Normalized() != saved);
    private readonly PageAction _detectNow = new();
    private readonly PageAction _saveOverride = new();

    private const int DetectionsTab = 1;

    // The detection the URL fragment names, such as #detection-0199a3c4-….
    private Guid? _selectedDetectionId;
    private int _activeTab;

    /// <summary>The video file's hash, as the address names it.</summary>
    [Parameter]
    public string Hash { get; set; } = "";


    private string Title => (_loaded, _videoFile) switch
    {
        (true, null) => "Video File Not Found",
        (_, { } videoFile) => LastKnownPath(videoFile) is { } path ? Path.GetFileName(path) : videoFile.FileHash.Value,
        _ => "Video File",
    };

    private static string? LastKnownPath(VideoFile videoFile) =>
        videoFile.FilePaths.FirstOrDefault()?.Path.Value
        ?? videoFile.Detections.OrderByDescending(detection => detection.StartedAt).Select(detection => detection.Path?.Value).FirstOrDefault(path => path is not null);

    private string? Folder => _videoFile?.FilePaths.FirstOrDefault() is { } filePath ? Path.GetDirectoryName(filePath.Path.Value) : null;

    private static readonly RenderFragment ArchivedOverrideDescription = builder =>
        builder.AddContent(0, "The override is kept while the video file is archived, and applies again when a scan finds the file.");

    private string SizeText => _videoFile?.Size.ToFileSizeText() ?? "";

    private string NoResultText => _videoFile switch
    {
        { FilePaths.Count: 0 } => "No detection result, and no file path to detect it from.",
        { LastFailure: not null } => "No detection result. The last detection failed.",
        _ => "No detection result yet. The file is waiting in the detection queue.",
    };

    /// <summary>How many samples agree, and on what picture size, which is what the confidence measures.</summary>
    private string AgreementText =>
        _agreeingSamples.FirstOrDefault()?.Box is { } box
            ? $"{_agreeingSamples.Count.ToCountText()} of {_samples.Count.ToCountText("sample", "samples")} agree on a picture of about {box.Width} × {box.Height}."
                + (_agreeingSamples.Count < _samples.Count ? " Those that differ lower the confidence." : "")
            : $"None of the {_samples.Count.ToCountText()} samples found a picture.";

    private string StatusExplanation(VideoFile videoFile) => (videoFile.Archived, _override.Saved?.DontSend) switch
    {
        (true, _) => "The video file left the library when no file path was left. "
            + "Debarr keeps its result, override and history, and restores them when a scan finds a file with its hash again.",
        (_, true) => "The override says don't send, so a playback sends nothing.",
        _ => VideoFileStatusText.Explain(videoFile.Status),
    };

    private static string NoFilePathsText(VideoFile videoFile) => videoFile.Archived
        ? "No file paths. The video file was archived when its last file path left the library."
        : "No file paths. The last scan found none, and the next library scan archives the video file if it still has none.";

    protected override IReadOnlySet<string> ReadModels { get; } = new HashSet<string>
    {
        nameof(VideoFile),
        nameof(DetectionSettings),
        nameof(Library),
        PlaybackRowProjection.ReadModel,
        HistoryClearProjection.ReadModel,
    };

    protected override bool Shows(ReadModelChanged change) =>
        base.Shows(change) && (change.ReadModel != nameof(VideoFile) || ChangesThisVideoFile(change));

    private bool ChangesThisVideoFile(ReadModelChanged change) =>
        FileHash.TryParse(Hash, out var fileHash) && change.Streams.Contains(fileHash.StreamId.ToString());

    protected override async Task ReloadAsync(CancellationToken cancellationToken)
    {
        FileHash? fileHash = FileHash.TryParse(Hash, out var parsed) ? parsed : null;
        await using (var session = store.QuerySession())
        {
            _standardRatios = (await DetectionSettings.ReadAsync(session, cancellationToken)).StandardRatios;
            _rootPaths = [.. (await Library.ReadAsync(session, cancellationToken)).RootFolders.Select(root => root.Path.Value)];
            if (fileHash is { } hash)
            {
                _videoFile = await VideoFile.ReadAsync(session, hash, cancellationToken);
                _playbacks = await session.ReadVideoFilePlaybacksAsync(hash, cancellationToken);
            }
            else
            {
                (_videoFile, _playbacks) = (null, []);
            }
        }

        _detections = [.. (_videoFile?.Detections ?? [])
            .OrderByDescending(detection => detection.StartedAt)
            .ThenByDescending(detection => detection.Id)];
        _loaded = true;

        if (_videoFile is null)
        {
            return;
        }

        _detectionsShowPaths = ShowsPaths(_videoFile, _detections.Select(detection => detection.Path?.Value));
        _playbacksShowPaths = ShowsPaths(_videoFile, _playbacks.Select(playback => playback.LocalPath?.Value));
        _samples = _videoFile.CurrentResult?.Result?.Samples ?? [];
        _agreeingSamples = [.. _samples.AgreeingSamples()];

        _override.Take(OverrideForm.From(_videoFile.Override));
    }

    protected override void OnInitialized()
    {
        _selectedDetectionId = SelectedDetectionId(navigation.Uri);
        if (_selectedDetectionId is not null)
        {
            _activeTab = DetectionsTab;
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        // The same component instance shows each video file a link opens.
        if (_loaded && _videoFile?.FileHash.Value != Hash)
        {
            _override.Clear();
            await ReloadAsync(CancellationToken.None);
        }
    }

    /// <summary>The detection id in the URI's fragment; null when the fragment names none.</summary>
    private static Guid? SelectedDetectionId(string uri)
    {
        const string prefix = "#detection-";
        var fragment = new Uri(uri).Fragment;
        return fragment.StartsWith(prefix, StringComparison.Ordinal) && Guid.TryParse(fragment[prefix.Length..], out var detectionId)
            ? detectionId
            : null;
    }

    private bool IsManual => _override.Saved is { DontSend: true } or { AspectRatio: not null };

    private SnappedAspectRatio Snap(double rawAspectRatio) => _standardRatios.Snap(new AspectRatio(rawAspectRatio));

    /// <summary>The result's source, and the container ratio's match that decided whether the picture was measured, when the detection read the container.</summary>
    private string SourceText(DetectionResult detectionResult, ContainerMetadata? containerMetadata)
    {
        var containerAspectRatio = containerMetadata?.ContainerAspectRatio.Value;
        var snapped = containerAspectRatio is { } raw ? Snap(raw) : null;
        return (detectionResult.AspectRatioSource, snapped) switch
        {
            (AspectRatioSource.Detected, { ChecksPicture: true, Value: var match }) =>
                $"Detected, measured from the picture because the container ratio {containerAspectRatio!.Value.ToRawAspectRatioText()} matches {match.ToAspectRatioText()}, which is marked Check Picture",
            (AspectRatioSource.Detected, _) => "Detected, measured from the picture",
            (AspectRatioSource.FromFile, { ChecksPicture: false, Match: null }) =>
                $"From File, the ratio the file states, since the container ratio {containerAspectRatio!.Value.ToRawAspectRatioText()} matches no standard ratio",
            (AspectRatioSource.FromFile, { ChecksPicture: false, Value: var match }) =>
                $"From File, the ratio the file states, since {match.ToAspectRatioText()} is not marked Check Picture",
            (AspectRatioSource.FromFile, _) => "From File, the ratio the file states",
            _ => "",
        };
    }

    private string ConfidenceText(DetectionResult detectionResult) => detectionResult.AspectRatioSource switch
    {
        AspectRatioSource.Detected when _samples.Count > 0 => $"{_agreeingSamples.Count} of {_samples.Count} samples agree",
        AspectRatioSource.FromFile => "for a ratio the file states",
        _ => "",
    };

    private static string OriginExplanation(DetectionOrigin origin) => origin switch
    {
        DetectionOrigin.Queue => "The detection queue ran it, as it does for every new file.",
        DetectionOrigin.DetectNow => "Started with Detect Now.",
        DetectionOrigin.StandardRatiosChange => "Matched again after the standard ratios changed, without reading the file.",
        _ => "",
    };

    private static string DetectorExplanation(int detectorVersion) =>
        detectorVersion == AspectRatioDetector.Version
            ? $"Detector version {detectorVersion}, the current one."
            : $"Detector version {detectorVersion}. Re-detect All in Settings > Detection replaces it with a result from version {AspectRatioDetector.Version}.";

    /// <summary>The root folder that holds the path; null when no root does.</summary>
    private string? RootFolderOf(string path)
    {
        var file = new FileInfo(path);
        return _rootPaths
            .Where(rootPath => file.IsSameOrUnder(new DirectoryInfo(rootPath)))
            .MaxBy(rootPath => rootPath.Length);
    }

    /// <summary>Media, searched for every path under the root.</summary>
    private static string MediaUnder(string rootPath) =>
        "?search=" + Uri.EscapeDataString(Path.TrimEndingDirectorySeparator(rootPath) + Path.DirectorySeparatorChar);

    private void SelectDetection(Guid detectionId)
    {
        _selectedDetectionId = detectionId;
        _activeTab = DetectionsTab;
    }

    /// <summary>Whether a table needs its Path column: true when a row names a path other than the video file's one file path.</summary>
    private static bool ShowsPaths(VideoFile videoFile, IEnumerable<string?> paths) =>
        videoFile.FilePaths is not [var filePath] || paths.Any(path => path != filePath.Path.Value);

    private string DetectionHref(Guid detectionId) => $"video-file/{Hash}#{DetectionElementId(detectionId)}";

    private static string DetectionElementId(Guid detectionId) => $"detection-{detectionId}";

    private async Task DetectNowAsync()
    {
        if (_videoFile is { } videoFile)
        {
            await _detectNow.RunAsync(() => detectionOrchestrator.DetectNowAsync(videoFile.FileHash, CancellationToken.None), Logger, "Detect Now did not start.");
        }
    }

    private Task SaveOverrideAsync() => _saveOverride.RunAsync(
        () => _override.SaveAsync(async model => _videoFile is null
            ? Result.Fail("The video file no longer exists.")
            : await runtime.SendCommandAsync(new SaveOverride(_videoFile.FileHash, model.AspectRatio, model.DontSend, model.Note), CancellationToken.None)),
        Logger,
        "The override was not saved.");
}
