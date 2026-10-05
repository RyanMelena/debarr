using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using Debarr.Extensions;
using FluentResults;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Debarr.Components;

/// <summary>
/// A modal that edits one integration, inside an <see cref="IntegrationModal"/>.
/// Test, Save and Remove run one at a time with the buttons disabled, and closing the modal cancels the one in flight.
/// A failure keeps the form as the operator left it: a refused field shows its error beneath it, turning Show Advanced on when it hides the field, and anything else shows above the buttons.
/// </summary>
public abstract class IntegrationModalBase : ComponentBase, IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private CancellationToken _cancellationToken;

    [CascadingParameter]
    private IMudDialogInstance Dialog { get; set; } = default!;

    [CascadingParameter]
    protected ShowAdvanced ShowAdvanced { get; private set; } = default!;

    [Inject]
    private IDialogService DialogService { get; set; } = default!;

    [Inject]
    private ILoggerFactory LoggerFactory { get; set; } = default!;

    private ILogger Logger => field ??= LoggerFactory.CreateLogger(GetType());

    /// <summary>Test, Save and Remove, which share the modal's buttons and its error.</summary>
    public PageAction ModalAction { get; } = new();

    public string? TestResult { get; private set; }

    public bool TestSucceeded { get; private set; }

    /// <summary>The fields that show only with Show Advanced on, by the names their refusals carry.</summary>
    public virtual IReadOnlyCollection<string> AdvancedFields => [];

    /// <summary>The title of the dialog that confirms Remove.</summary>
    protected abstract string RemoveTitle { get; }

    /// <summary>The question the dialog that confirms Remove asks.</summary>
    protected abstract string RemoveMessage { get; }

    /// <summary>Refuses unsaved values the command would refuse, all at once and each beneath its field, before Test tries them.</summary>
    protected abstract Task<Result> ValidateIntegrationAsync(CancellationToken cancellationToken);

    /// <summary>Tries the unsaved values, which <see cref="ValidateIntegrationAsync"/> accepted, and returns whether they worked and the message that says so.</summary>
    protected abstract Task<(bool Succeeded, string? Message)> TestIntegrationAsync(CancellationToken cancellationToken);

    protected abstract Task<ResultBase> SaveIntegrationAsync(CancellationToken cancellationToken);

    protected abstract Task<ResultBase> RemoveIntegrationAsync(CancellationToken cancellationToken);

    protected override void OnInitialized() =>
        _cancellationToken = new CancellationDisposable().DisposeWith(_disposables).Token;

    public void Cancel() => Dialog.Cancel();

    public Task TestAsync() => RunAsync(async () =>
    {
        TestResult = null;
        var validated = await ValidateIntegrationAsync(_cancellationToken);
        if (validated.IsSuccess)
        {
            (TestSucceeded, TestResult) = await TestIntegrationAsync(_cancellationToken);
        }

        return validated;
    }, "The test could not run.");

    public Task SaveAsync() => RunAsync(async () =>
    {
        TestResult = null;
        var saved = await SaveIntegrationAsync(_cancellationToken);
        if (saved.IsSuccess)
        {
            Dialog.Close();
        }

        return saved;
    }, "The changes were not saved.");

    public async Task RemoveAsync()
    {
        if (await DialogService.ConfirmAsync(RemoveTitle, RemoveMessage, "Remove"))
        {
            await RunAsync(async () =>
            {
                var removed = await RemoveIntegrationAsync(_cancellationToken);
                if (removed.IsSuccess)
                {
                    Dialog.Close();
                }

                return removed;
            }, "Nothing was removed.");
        }
    }

    /// <summary>Runs one action with the modal's buttons disabled, and ends quietly when closing the modal cancels it.</summary>
    private async Task RunAsync(Func<Task<ResultBase>> action, string failure)
    {
        try
        {
            await ModalAction.RunAsync(action, Logger, failure);
            ShowAdvanced.RevealRefused(ModalAction, AdvancedFields);
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
        GC.SuppressFinalize(this);
    }
}
