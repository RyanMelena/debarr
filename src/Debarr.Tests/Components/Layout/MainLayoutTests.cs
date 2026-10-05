using Bunit;
using Debarr.Components.Layout;
using Debarr.Tests.Extensions;
using Microsoft.AspNetCore.Components;

namespace Debarr.Tests.Components.Layout;

public sealed class MainLayoutTests : PageTestContext
{
    private static readonly RenderFragment BrokenPage = Page(() => true);

    private static readonly RenderFragment WorkingPage = Page(() => false);

    [Fact]
    public void A_page_that_throws_says_it_stopped_working_and_offers_to_try_again()
    {
        var cut = Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, BrokenPage));

        Assert.Contains("This page stopped working: The page broke.", cut.Find("#page-failure").TextContent);
        Assert.Equal("Something Went Wrong", cut.Find("#page-title").TextContent.Trim());
        Assert.Single(cut.FindAll("#page-failure-retry"));
    }

    [Fact]
    public void The_error_clears_when_the_operator_navigates_to_another_page()
    {
        var cut = Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, BrokenPage));
        cut.WaitForElement("#page-failure", Timeout);

        // The router renders the layout again with the next page as its body.
        cut.Render(parameters => parameters.Add(layout => layout.Body, WorkingPage));

        Assert.Empty(cut.FindAll("#page-failure"));
        Assert.Single(cut.FindAll("#working-page"));
    }

    [Fact]
    public async Task Try_again_renders_the_page_again()
    {
        var renders = 0;
        var cut = Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, Page(() => renders++ == 0)));
        cut.WaitForElement("#page-failure", Timeout);

        await cut.RaiseClickAsync("#page-failure-retry", Timeout);

        cut.WaitForElement("#working-page", Timeout);
        Assert.Empty(cut.FindAll("#page-failure"));
    }

    private static RenderFragment Page(Func<bool> throws) => builder =>
    {
        builder.OpenComponent<TestPage>(0);
        builder.AddComponentParameter(1, nameof(TestPage.Throws), throws);
        builder.CloseComponent();
    };

    /// <summary>A page that throws while it renders when <see cref="Throws"/> says to.</summary>
    private sealed class TestPage : ComponentBase
    {
        [Parameter]
        public Func<bool> Throws { get; set; } = () => false;

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            if (Throws())
            {
                throw new InvalidOperationException("The page broke.");
            }

            builder.AddMarkupContent(0, "<p id=\"working-page\">Working</p>");
        }
    }
}
