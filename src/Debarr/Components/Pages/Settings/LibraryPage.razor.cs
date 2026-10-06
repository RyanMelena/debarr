using Debarr.Activity;
using Debarr.EventStore;
using Debarr.Extensions;
using Debarr.Scanning;
using Fisher;
using MudBlazor;
using Wolverine.Runtime;

namespace Debarr.Components.Pages.Settings;

public partial class LibraryPage(IDocumentStore store, RootFolderRemover rootFolderRemover, IWolverineRuntime runtime, TimeProvider timeProvider, IDialogService dialogService)
{
    private IReadOnlyList<RootFolderRow> _roots = [];
    private readonly EditedForm<LibrarySettingsForm> _form = new(model => model with { });
    private Dictionary<string, int> _filePathCounts = [];
    private MudForm? _mudForm;
    private string _newRootFolderPath = "";
    private readonly PageAction _save = new();

    // Adding, enabling and removing a root folder, which apply at once.
    private readonly PageAction _rootFolderAction = new();

    private bool Busy => _save.Running || _rootFolderAction.Running;

    private string? NewRootFolderError => _rootFolderAction.FieldError(nameof(AddRootFolder.Path));

    protected override IReadOnlySet<string> ReadModels { get; } = new HashSet<string>
    {
        nameof(Library),
        LibraryScanSummaryRowProjection.ReadModel,
        StoredFilePathProjection.ReadModel,
    };

    protected override bool ShowsActivity(ActivityEvent activityEvent) => RootFolderRemover.ChangesRunningRootFolderRemoval(activityEvent);

    protected override async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        _roots = RootFolderRow.Of(await session.ReadRootFoldersAsync(cancellationToken), rootFolderRemover.RunningRootFolderRemoval);
        _filePathCounts = [];
        foreach (var row in _roots)
        {
            _filePathCounts[row.Path] = await session.CountUnderAsync(row.Path, cancellationToken);
        }

        _form.Take(LibrarySettingsForm.FromSettings((await Library.ReadAsync(session, cancellationToken)).Settings));
    }

    private static string? ValidateVideoExtensions(string videoExtensions) =>
        VideoExtensions.Parse(videoExtensions).Errors.FirstOrDefault()?.Message;

    private async Task SaveAsync()
    {
        if (_mudForm is null)
        {
            return;
        }

        await _mudForm.ValidateAsync();
        if (!_mudForm.IsValid)
        {
            return;
        }

        await _save.RunAsync(() => _form.SaveAsync(model => runtime.SendCommandAsync(model.ToChangeLibrarySettings(), CancellationToken.None)), Logger, "The settings were not saved.");
    }

    /// <summary>Takes the typed folder, and clears the refusal of the folder typed before it.</summary>
    private void SetNewRootFolderPath(string path)
    {
        _newRootFolderPath = path;
        _rootFolderAction.ClearFieldError(nameof(AddRootFolder.Path));
    }

    /// <summary>Opens the folder browser from the typed folder, and puts the folder the operator chooses in the field.</summary>
    private async Task ChooseRootFolderAsync()
    {
        if (await dialogService.ChooseFolderAsync(_newRootFolderPath) is { } folder)
        {
            SetNewRootFolderPath(folder);
        }
    }

    private Task AddRootFolderAsync() => _rootFolderAction.RunAsync(async () =>
    {
        var command = AddRootFolder.Parse(_newRootFolderPath, timeProvider.GetUtcNow());
        var added = command.IsFailed ? command.ToResult() : await runtime.SendCommandAsync(command.Value, CancellationToken.None);
        if (added.IsSuccess)
        {
            _newRootFolderPath = "";
        }

        return added;
    }, Logger, "The root folder was not added.");

    private Task SetRootFolderEnabledAsync(RootFolder root, bool enabled) =>
        _rootFolderAction.RunAsync(
            () => runtime.SendCommandAsync(new SetRootFolderEnabled(root.Path, enabled), CancellationToken.None),
            Logger,
            "The root folder was not changed.");

    private async Task RemoveRootFolderAsync(RootFolder root)
    {
        var confirmed = await dialogService.ConfirmAsync(
            "Remove Root Folder",
            $"Remove {root.Path.Value}? Its file paths are removed now, and every video file left with no file path is archived. A scan that finds one again restores it with its result, override and history.",
            "Remove");
        if (confirmed)
        {
            await _rootFolderAction.RunAsync(
                async () =>
                {
                    var removed = await runtime.SendCommandAsync(new RemoveRootFolder(root.Path), CancellationToken.None);
                    if (removed.IsSuccess)
                    {
                        await ReloadAsync(CancellationToken.None);
                    }

                    return removed;
                },
                Logger,
                "The root folder was not removed.");
        }
    }
}
