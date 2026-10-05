using Bunit;
using Debarr.Components;
using MudBlazor;
using MudBlazor.Services;

namespace Debarr.Tests.Components;

public sealed class StateLabelTests : BunitContext
{
    public StateLabelTests() => Services.AddMudServices();

    [Fact]
    public void A_label_shows_its_state_in_the_state_colour_after_an_icon()
    {
        var cut = Render<StateLabel>(parameters => parameters
            .Add(label => label.Icon, Icons.Material.Outlined.ErrorOutline)
            .Add(label => label.Color, Color.Error)
            .Add(label => label.Class, "last-scan")
            .AddChildContent("Failed"));

        var label = cut.Find(".state-label");
        Assert.Contains("mud-error-text", label.ClassList);
        Assert.Contains("last-scan", label.ClassList);
        Assert.Equal("true", label.QuerySelector("svg")?.GetAttribute("aria-hidden"));
        Assert.Equal("Failed", label.TextContent.Trim());
    }

    [Fact]
    public void A_label_without_a_colour_shows_in_secondary_text()
    {
        var cut = Render<StateLabel>(parameters => parameters
            .Add(label => label.Icon, Icons.Material.Outlined.Block)
            .AddChildContent("Not sent"));

        Assert.Contains("mud-text-secondary", cut.Find(".state-label").ClassList);
    }

    [Fact]
    public void A_change_of_state_fades_the_new_state_in()
    {
        var cut = Render<StateLabel>(parameters => parameters
            .Add(label => label.Icon, Icons.Material.Outlined.HourglassEmpty)
            .AddChildContent("Pending"));
        Assert.DoesNotContain("state-label-changed", cut.Find(".state-label").ClassList);

        cut.Render(parameters => parameters
            .Add(label => label.Icon, Icons.Material.Outlined.Sync)
            .Add(label => label.Color, Color.Info)
            .AddChildContent("Detecting"));

        Assert.Contains("state-label-changed", cut.Find(".state-label").ClassList);
    }

    [Fact]
    public void A_label_whose_text_changes_in_the_same_state_stays_still()
    {
        var cut = Render<StateLabel>(parameters => parameters
            .Add(label => label.Icon, Icons.Material.Outlined.ErrorOutline)
            .Add(label => label.Color, Color.Error)
            .AddChildContent("Failed"));

        cut.Render(parameters => parameters.AddChildContent("Failed again"));

        Assert.DoesNotContain("state-label-changed", cut.Find(".state-label").ClassList);
    }
}
