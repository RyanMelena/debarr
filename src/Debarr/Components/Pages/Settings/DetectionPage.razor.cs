using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Extensions;
using Fisher;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wolverine.Runtime;

namespace Debarr.Components.Pages.Settings;

public partial class DetectionPage(IDocumentStore store, IWolverineRuntime runtime, IDialogService dialogService)
{
    private readonly EditedForm<DetectionSettingsForm> _form = new(model => model.Copy(), (edited, saved) => edited.HasChangesFrom(saved));
    private int _olderVersionCount;
    private double? _newAspectRatio;
    private string? _newAspectRatioError;
    private readonly PageAction _save = new();
    private readonly PageAction _redetectAll = new();

    [CascadingParameter]
    private ShowAdvanced ShowAdvanced { get; set; } = default!;

    /// <summary>The settings that show only with Show Advanced on.</summary>
    private static readonly string[] AdvancedFields =
    [
        nameof(DetectionSettingsForm.TimeoutSeconds),
        nameof(DetectionSettingsForm.SampleCount),
        nameof(DetectionSettingsForm.SkipStartAndEndPercent),
        nameof(DetectionSettingsForm.BlackLevelSdr),
        nameof(DetectionSettingsForm.BlackLevelHdr),
        nameof(DetectionSettingsForm.MatchTolerance),
    ];

    private bool Busy => _save.Running || _redetectAll.Running;

    private string OlderVersionText => _olderVersionCount == 1
        ? "1 detection result is from an older version."
        : $"{_olderVersionCount.ToCountText()} detection results are from an older version.";

    protected override IReadOnlySet<string> ReadModels { get; } = new HashSet<string> { nameof(DetectionSettings), MediaRowProjection.ReadModel };

    protected override async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        _form.Take(DetectionSettingsForm.FromSettings(await DetectionSettings.ReadAsync(session, cancellationToken)));
        _olderVersionCount = await session.CountResultsBeforeAsync(AspectRatioDetector.Version, cancellationToken);
    }

    /// <summary>The refusal of the typed ratio, or of the standard ratios the last save refused.</summary>
    private string? NewAspectRatioError => _newAspectRatioError ?? _save.FieldError(nameof(StandardRatios));

    /// <summary>Takes the typed ratio, and clears the refusal of the ratio typed before it.</summary>
    private void SetNewAspectRatio(double? aspectRatio)
    {
        _newAspectRatio = aspectRatio;
        _newAspectRatioError = null;
    }

    private void AddAspectRatio(DetectionSettingsForm model)
    {
        if (_newAspectRatio is not { } aspectRatio || aspectRatio <= 0)
        {
            return;
        }

        if (model.StandardRatios.Any(row => Math.Round(row.AspectRatio, 2) == Math.Round(aspectRatio, 2)))
        {
            _newAspectRatioError = $"{aspectRatio.ToAspectRatioText()} is a standard ratio already.";
            return;
        }

        model.StandardRatios = [.. model.StandardRatios.Append(new StandardRatioForm { AspectRatio = aspectRatio }).OrderBy(row => row.AspectRatio)];
        _newAspectRatio = null;
        _newAspectRatioError = null;
        _save.ClearFieldError(nameof(StandardRatios));
    }

    private void RemoveAspectRatio(DetectionSettingsForm model, StandardRatioForm row)
    {
        model.StandardRatios.Remove(row);
        _save.ClearFieldError(nameof(StandardRatios));
    }

    private async Task SaveAsync()
    {
        await _save.RunAsync(() => _form.SaveAsync(model => runtime.SendCommandAsync(model.ToChangeDetectionSettings(), CancellationToken.None)), Logger, "The settings were not saved.");
        ShowAdvanced.RevealRefused(_save, AdvancedFields);
    }

    private async Task RedetectAllAsync()
    {
        var confirmed = await dialogService.ConfirmAsync(
            "Re-detect All",
            "Clear every detection result and failure, and detect every video file again? Overrides are kept.",
            "Re-detect All");
        if (confirmed)
        {
            await _redetectAll.RunAsync(() => runtime.SendCommandAsync(new RedetectAll(), CancellationToken.None), Logger, "Re-detect All did not run.");
        }
    }
}
