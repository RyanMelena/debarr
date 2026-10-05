using System.Reactive;
using System.Reactive.Linq;
using Debarr.Extensions;
using Microsoft.AspNetCore.Components;

namespace Debarr.Components;

/// <summary>The whole seconds since a moment, at least one, rendered again each second.</summary>
public sealed partial class ElapsedTime(TimeProvider timeProvider)
{
    [Parameter, EditorRequired]
    public DateTimeOffset Since { get; set; }

    private string Text =>
        TimeSpan.FromSeconds(Math.Max(1, Math.Floor((timeProvider.GetUtcNow() - Since).TotalSeconds))).ToDisplayText();

    protected override IObservable<Unit> Reloads =>
        Observable.Interval(TimeSpan.FromSeconds(1), Scheduler).Select(_ => Unit.Default);

    protected override Task ReloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
