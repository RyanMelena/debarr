using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using Debarr.Activity;
using Debarr.Extensions;
using Microsoft.AspNetCore.Components;

namespace Debarr.Components;

/// <summary>A component that reloads and renders again on each of its <see cref="Reloads"/>.</summary>
public abstract class LiveComponentBase : ComponentBase, IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    [Inject]
    private ActivityFeed ActivityFeed { get; set; } = default!;

    [Inject]
    protected IScheduler Scheduler { get; private set; } = default!;

    [Inject]
    private ILoggerFactory LoggerFactory { get; set; } = default!;

    /// <summary>The page's logger, which records the failure of an action the operator started.</summary>
    protected ILogger Logger => field ??= LoggerFactory.CreateLogger(GetType());

    /// <summary>The read models the component reads, by the names <see cref="ReadModelChanged"/> carries.</summary>
    protected virtual IReadOnlySet<string> ReadModels { get; } = new HashSet<string>();

    protected virtual bool Shows(ReadModelChanged change) => ReadModels.Contains(change.ReadModel);

    protected virtual bool ShowsActivity(ActivityEvent activityEvent) => false;

    /// <summary>
    /// When the component reloads: after a commit that changes a read model it reads, or a runtime event it shows, at most once per 250 ms.
    /// A component whose values change with neither, such as a log file, reloads on a timer instead.
    /// </summary>
    protected virtual IObservable<Unit> Reloads =>
        ActivityFeed.Events
            .Where(activityEvent => activityEvent is ReadModelChanged change ? Shows(change) : ShowsActivity(activityEvent))
            .Sample(TimeSpan.FromMilliseconds(250), Scheduler)
            .Select(_ => Unit.Default);

    /// <summary>Reads the component's state, once at initialization and again on each of its <see cref="Reloads"/>.</summary>
    protected abstract Task ReloadAsync(CancellationToken cancellationToken);

    protected override async Task OnInitializedAsync()
    {
        Reloads
            .Select(_ => Observable.FromAsync(cancellationToken => InvokeAsync(() => RenderReloadAsync(cancellationToken))))
            .Switch()
            .CompleteOnError(exception => _ = DispatchExceptionAsync(exception))
            .Subscribe()
            .DisposeWith(_disposables);
        var cancellation = new CancellationDisposable().DisposeWith(_disposables);

        try
        {
            await ReloadAsync(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsDisposed)
        {
        }
    }

    private async Task RenderReloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ReloadAsync(cancellationToken);
            StateHasChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await DispatchExceptionAsync(exception);
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
        GC.SuppressFinalize(this);
    }
}
