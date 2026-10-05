using Bunit;
using Debarr.Components;

namespace Debarr.Tests.Components;

public sealed class CardButtonTests : BunitContext
{
    [Fact]
    public async Task A_card_is_a_native_button_so_tab_reaches_it_and_enter_or_space_clicks_it()
    {
        var clicks = 0;
        var cut = Render<CardButton>(parameters => parameters
            .Add(card => card.Class, "player-card")
            .Add(card => card.OnClick, () => clicks++)
            .AddUnmatched("aria-label", "Add Player")
            .AddChildContent("<h6>Theater</h6>"));

        var button = cut.Find("button.card-button.player-card");
        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Equal("Add Player", button.GetAttribute("aria-label"));
        Assert.Equal("Theater", button.TextContent);
        await button.ClickAsync();
        Assert.Equal(1, clicks);
    }
}
