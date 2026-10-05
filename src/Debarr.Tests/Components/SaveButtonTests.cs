using Bunit;
using Debarr.Components.Pages.Settings;
using Debarr.Tests.Extensions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Debarr.Tests.Components;

public sealed class SaveButtonTests : PageTestContext
{
    [Fact]
    public async Task Save_is_enabled_only_while_the_page_has_unsaved_changes()
    {
        var cut = RenderPage<LibraryPage>();

        cut.WaitForAssertion(() => Assert.True(cut.Find("#library-save").HasAttribute("disabled")), Timeout);
        await cut.RaiseInputAsync("#library-video-extensions", "mkv mp4 avi", Timeout);

        cut.WaitForAssertion(() => Assert.False(cut.Find("#library-save").HasAttribute("disabled")), Timeout);
    }

    [Fact]
    public async Task Leaving_with_unsaved_changes_asks_first_and_stays_when_cancelled()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        var cut = RenderPage<LibraryPage>();
        await cut.RaiseInputAsync("#library-video-extensions", "mkv mp4 avi", Timeout);

        navigation.NavigateTo("system/status");

        cut.WaitForAssertion(() => Assert.Contains("changes that are not saved", cut.Find("#confirm-message").TextContent), Timeout);
        Assert.Equal("Discard Changes", cut.Find("#confirm-accept").TextContent.Trim());
        await cut.RaiseClickAsync("#confirm-cancel", Timeout);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#confirm-message")), Timeout);
        Assert.DoesNotContain("system/status", navigation.Uri);
    }

    [Fact]
    public async Task Leaving_with_unsaved_changes_leaves_once_the_operator_discards_them()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        var cut = RenderPage<LibraryPage>();
        await cut.RaiseInputAsync("#library-video-extensions", "mkv mp4 avi", Timeout);

        navigation.NavigateTo("system/status");
        await cut.RaiseClickAsync("#confirm-accept", Timeout);

        // Leaving renders nothing, so the URI is polled rather than checked after each render.
        await Poll.UntilAsync(() => navigation.Uri.EndsWith("system/status", StringComparison.Ordinal), Timeout);
    }

    [Fact]
    public async Task Leaving_without_unsaved_changes_asks_nothing()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        var cut = RenderPage<LibraryPage>();
        cut.WaitForElement("#library-video-extensions", Timeout);

        navigation.NavigateTo("system/status");

        // The navigation waits for the renderer while the page is still rendering, so the URI is polled.
        await Poll.UntilAsync(() => navigation.Uri.EndsWith("system/status", StringComparison.Ordinal), Timeout);
        Assert.Empty(cut.FindAll("#confirm-message"));
    }
}
