using Bunit;
using Debarr.Activity;
using Debarr.Appearance;
using Debarr.Components;
using Debarr.Components.Layout;
using Debarr.EventStore;
using Debarr.Health;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Reactive.Testing;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Services;
using Wolverine.Runtime;
using IScheduler = System.Reactive.Concurrency.IScheduler;

namespace Debarr.Tests.Components.Layout;

public sealed class UISettingsProviderTests : BunitContext, IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 18, 0, 0, TimeSpan.Zero);

    private readonly TestScheduler _scheduler = new();
    private readonly DebarrWebApplicationFactory _factory = new();
    private readonly IServiceScope _appScope;
    private readonly IWolverineRuntime _runtime;

    /// <summary>Renders against the booted app, with the test's clock, scheduler and activity feed in place of the app's.</summary>
    public UISettingsProviderTests()
    {
        var timeProvider = new FakeTimeProvider(Now);
        timeProvider.SetLocalTimeZone(TimeZoneInfo.Utc);
        _appScope = _factory.Services.CreateScope();
        _runtime = _factory.Services.GetRequiredService<IWolverineRuntime>();

        Services.AddMudServices();
        Services.AddSingleton<TimeProvider>(timeProvider);
        Services.AddSingleton(new ActivityFeed([_factory.Services.GetRequiredService<ReadModelChangeListener>()]));
        Services.AddSingleton<IScheduler>(_scheduler);
        Services.AddSingleton(StartedHealthCheckService());
        Services.AddFallbackServiceProvider(_appScope.ServiceProvider);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static CancellationToken CancellationToken => Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_save_re_renders_the_dates_an_open_page_shows()
    {
        var cut = Render<UISettingsProvider>(parameters => parameters.AddChildContent<DatePage>());
        cut.WaitForAssertion(() => Assert.Equal("Today 17:30", cut.Find("#date").TextContent));

        await _runtime.SendCommandAsync(new SaveUISettings(UITheme.Auto, DateTimeFormats.Default with { ShortDateFormat = "yyyy-MM-dd", TimeFormat = "h:mm tt" }, false), CancellationToken);
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(250).Ticks);

        cut.WaitForAssertion(() => Assert.Equal("2026-09-27 5:30 PM", cut.Find("#date").TextContent));
    }

    [Fact]
    public async Task A_save_applies_the_theme_to_the_open_layout()
    {
        JSInterop.Setup<bool>("mudThemeProvider.isDarkMode").SetResult(false);
        await _runtime.SendCommandAsync(new SaveUISettings(UITheme.Dark, DateTimeFormats.Default, true), CancellationToken);
        var cut = Render<UISettingsProvider>(parameters => parameters.AddChildContent<MainLayout>());
        cut.WaitForAssertion(() => Assert.True(IsDarkMode(cut)));

        await _runtime.SendCommandAsync(new SaveUISettings(UITheme.Light, DateTimeFormats.Default, true), CancellationToken);
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(250).Ticks);
        cut.WaitForAssertion(() => Assert.False(IsDarkMode(cut)));

        // Auto follows the browser, which prefers light here.
        await _runtime.SendCommandAsync(new SaveUISettings(UITheme.Auto, DateTimeFormats.Default, true), CancellationToken);
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(250).Ticks);
        cut.WaitForAssertion(() => Assert.False(IsDarkMode(cut)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_auto_theme_follows_the_browser_preference(bool browserIsDark)
    {
        JSInterop.Setup<bool>("mudThemeProvider.isDarkMode").SetResult(browserIsDark);

        var cut = Render<UISettingsProvider>(parameters => parameters.AddChildContent<MainLayout>());

        cut.WaitForAssertion(() => Assert.Equal(browserIsDark, IsDarkMode(cut)));
    }

    private static bool IsDarkMode(IRenderedComponent<UISettingsProvider> cut) =>
        cut.FindComponent<MudThemeProvider>().Instance.GetState(themeProvider => themeProvider.IsDarkMode);

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        _appScope.Dispose();
        await _factory.DisposeAsync();
    }

    private sealed class DatePage : ComponentBase
    {
        [CascadingParameter]
        private DateTimeFormatter Formatter { get; set; } = default!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddAttribute(1, "id", "date");
            builder.AddContent(2, Formatter.FormatDateTime(new DateTimeOffset(2026, 9, 27, 17, 30, 0, TimeSpan.Zero)));
            builder.CloseElement();
        }
    }

    /// <summary>A health check service with no checks, started so the layout's badge reads an empty list.</summary>
    private HealthCheckService StartedHealthCheckService()
    {
        var service = new HealthCheckService([], _scheduler, NullLogger<HealthCheckService>.Instance);
        _ = service.StartAsync(CancellationToken.None);
        return service;
    }
}
