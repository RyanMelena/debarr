using Bunit;
using Debarr.Appearance;
using Debarr.Components.Pages.Settings;
using Debarr.EventStore;
using Wolverine.Runtime;

namespace Debarr.Tests.Components.Pages.Settings;

public sealed class UIPageTests : PageTestContext
{
    [Fact]
    public async Task The_page_reloads_when_a_commit_changes_the_ui_settings()
    {
        var cut = RenderPage<UIPage>();
        cut.WaitForAssertion(() => Assert.Equal("Auto", cut.Find("#ui-theme").GetAttribute("value")), Timeout);

        var saved = await GetAppService<IWolverineRuntime>().SendCommandAsync(new SaveUISettings(UITheme.Dark, DateTimeFormats.Default, true), CancellationToken);
        Assert.True(saved.IsSuccess);

        cut.WaitForAssertion(() => Assert.Equal("Dark", cut.Find("#ui-theme").GetAttribute("value")), Timeout);
    }
}
