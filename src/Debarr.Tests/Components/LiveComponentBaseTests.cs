using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Bunit;
using Debarr.Activity;
using Debarr.Components;
using Debarr.Tests.Activity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Reactive.Testing;

namespace Debarr.Tests.Components;

public class LiveComponentBaseTests : BunitContext
{
    private const string ReadModelRead = "Read";
    private const string OtherReadModel = "Other";

    private readonly Subject<ActivityEvent> _source = new();
    private readonly TestScheduler _scheduler = new();

    public LiveComponentBaseTests()
    {
        Services.AddSingleton(new ActivityFeed([new TestActivitySource(_source)]));
        Services.AddSingleton<IScheduler>(_scheduler);
    }

    [Fact]
    public void A_burst_of_changes_to_a_read_model_it_reads_reloads_once()
    {
        var cut = RenderInsideErrorBoundary();
        Assert.Equal("1", cut.Find("#reloads").TextContent);

        _source.OnNext(Changed(ReadModelRead));
        _source.OnNext(Changed(ReadModelRead));
        _source.OnNext(Changed(ReadModelRead));
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(249).Ticks);
        Assert.Equal("1", cut.Find("#reloads").TextContent);

        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(1).Ticks);
        Assert.Equal("2", cut.Find("#reloads").TextContent);
    }

    [Fact]
    public void A_change_to_another_read_model_reloads_never()
    {
        var cut = RenderInsideErrorBoundary();

        _source.OnNext(Changed(OtherReadModel));
        _scheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks);

        Assert.Equal("1", cut.Find("#reloads").TextContent);
    }

    [Fact]
    public void An_activity_event_reloads_only_when_shows_activity_takes_it()
    {
        var cut = RenderInsideErrorBoundary();

        _source.OnNext(new RejectedEvent());
        _scheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks);
        Assert.Equal("1", cut.Find("#reloads").TextContent);

        _source.OnNext(new ShownEvent());
        _scheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks);
        Assert.Equal("2", cut.Find("#reloads").TextContent);
    }

    [Fact]
    public void A_reload_that_throws_reaches_the_error_boundary()
    {
        var cut = RenderInsideErrorBoundary(throwFromLaterReloads: true);

        _source.OnNext(Changed(ReadModelRead));
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(250).Ticks);

        Assert.Equal("Reload failed.", cut.Find("#error").TextContent);
    }

    [Fact]
    public void A_filter_that_throws_reaches_the_error_boundary()
    {
        var cut = RenderInsideErrorBoundary(throwFromFilter: true);

        _source.OnNext(Changed(ReadModelRead));

        Assert.Equal("Filter failed.", cut.Find("#error").TextContent);
    }

    [Fact]
    public async Task Disposing_the_component_ends_its_subscription()
    {
        var cut = RenderInsideErrorBoundary();
        _source.OnNext(Changed(ReadModelRead));
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(250).Ticks);
        var component = cut.FindComponent<CountingComponent>().Instance;
        Assert.Equal(2, component.ReloadCount);
        Assert.True(_source.HasObservers);

        await DisposeComponentsAsync();
        _source.OnNext(Changed(ReadModelRead));
        _scheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks);

        Assert.False(_source.HasObservers);
        Assert.Equal(2, component.ReloadCount);
    }

    [Fact]
    public void A_component_that_reloads_on_a_timer_reloads_each_tick_and_ignores_the_feed()
    {
        var cut = Render<TimedComponent>();
        Assert.Equal("1", cut.Find("#reloads").TextContent);

        _source.OnNext(Changed(ReadModelRead));
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(999).Ticks);
        Assert.Equal("1", cut.Find("#reloads").TextContent);

        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(1).Ticks);
        Assert.Equal("2", cut.Find("#reloads").TextContent);

        _scheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks);
        Assert.Equal("3", cut.Find("#reloads").TextContent);
    }

    private static ReadModelChanged Changed(string readModel) => new(readModel, new HashSet<string> { Guid.NewGuid().ToString() });

    private IRenderedComponent<IComponent> RenderInsideErrorBoundary(bool throwFromFilter = false, bool throwFromLaterReloads = false) =>
        Render(builder =>
        {
            builder.OpenComponent<ErrorBoundary>(0);
            builder.AddComponentParameter(1, nameof(ErrorBoundary.ChildContent), (RenderFragment)(child =>
            {
                child.OpenComponent<CountingComponent>(0);
                child.AddComponentParameter(1, nameof(CountingComponent.ThrowFromFilter), throwFromFilter);
                child.AddComponentParameter(2, nameof(CountingComponent.ThrowFromLaterReloads), throwFromLaterReloads);
                child.CloseComponent();
            }));
            builder.AddComponentParameter(2, nameof(ErrorBoundary.ErrorContent), (RenderFragment<Exception>)(exception => error =>
            {
                error.OpenElement(0, "p");
                error.AddAttribute(1, "id", "error");
                error.AddContent(2, exception.Message);
                error.CloseElement();
            }));
            builder.CloseComponent();
        });

    private sealed record ShownEvent : ActivityEvent;

    private sealed record RejectedEvent : ActivityEvent;

    private sealed class TimedComponent : LiveComponentBase
    {
        private int _reloads;

        protected override IReadOnlySet<string> ReadModels { get; } = new HashSet<string> { ReadModelRead };

        protected override IObservable<Unit> Reloads => Observable.Interval(TimeSpan.FromSeconds(1), Scheduler).Select(_ => Unit.Default);

        protected override Task ReloadAsync(CancellationToken cancellationToken)
        {
            _reloads++;
            return Task.CompletedTask;
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddAttribute(1, "id", "reloads");
            builder.AddContent(2, _reloads);
            builder.CloseElement();
        }
    }

    private sealed class CountingComponent : LiveComponentBase
    {
        [Parameter]
        public bool ThrowFromFilter { get; set; }

        [Parameter]
        public bool ThrowFromLaterReloads { get; set; }

        public int ReloadCount { get; private set; }

        protected override IReadOnlySet<string> ReadModels { get; } = new HashSet<string> { ReadModelRead };

        protected override bool Shows(ReadModelChanged change) =>
            ThrowFromFilter ? throw new InvalidOperationException("Filter failed.") : base.Shows(change);

        protected override bool ShowsActivity(ActivityEvent activityEvent) => activityEvent is ShownEvent;

        protected override Task ReloadAsync(CancellationToken cancellationToken)
        {
            if (ThrowFromLaterReloads && ReloadCount > 0)
            {
                throw new InvalidOperationException("Reload failed.");
            }

            ReloadCount++;
            return Task.CompletedTask;
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddAttribute(1, "id", "reloads");
            builder.AddContent(2, ReloadCount);
            builder.CloseElement();
        }
    }
}
