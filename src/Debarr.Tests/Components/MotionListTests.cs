using Bunit;
using Debarr.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Debarr.Tests.Components;

public sealed class MotionListTests : BunitContext
{
    private readonly FakeTimeProvider _timeProvider = new();

    public MotionListTests() => Services.AddSingleton<TimeProvider>(_timeProvider);

    [Fact]
    public void The_first_items_render_without_motion()
    {
        var cut = RenderList(["a", "b"]);

        Assert.Equal(["a:", "b:"], Items(cut));
    }

    [Fact]
    public void An_item_that_joins_the_list_enters_and_the_others_stay_still()
    {
        var cut = RenderList(["a", "c"]);

        SetItems(cut, ["a", "b", "c"]);

        Assert.Equal(["a:", "b:motion-enter", "c:"], Items(cut));
    }

    [Fact]
    public void An_item_that_leaves_keeps_its_place_until_its_animation_ends()
    {
        var cut = RenderList(["a", "b", "c"]);
        var items = cut.FindComponents<MotionListItem>().Select(item => item.Instance).ToList();

        SetItems(cut, ["a", "c"]);

        Assert.Equal(["a:", "b:motion-leave", "c:"], Items(cut));
        _timeProvider.Advance(MotionList<string>.LeaveDuration - TimeSpan.FromMilliseconds(1));
        Assert.Equal(3, Items(cut).Count);

        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        cut.WaitForAssertion(() => Assert.Equal(["a:", "c:"], Items(cut)));
        Assert.Equal([items[0], items[2]], cut.FindComponents<MotionListItem>().Select(item => item.Instance));
    }

    [Fact]
    public void Items_that_leave_one_after_another_stay_until_the_last_has_animated()
    {
        var cut = RenderList(["a", "b", "c"]);

        SetItems(cut, ["b", "c"]);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        SetItems(cut, ["c"]);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(100));

        Assert.Equal(["a:motion-leave", "b:motion-leave", "c:"], Items(cut));
        _timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        cut.WaitForAssertion(() => Assert.Equal(["c:"], Items(cut)));
    }

    [Fact]
    public void An_item_that_returns_while_it_leaves_enters_again()
    {
        var cut = RenderList(["a", "b"]);

        SetItems(cut, ["a"]);
        SetItems(cut, ["a", "b"]);
        _timeProvider.Advance(MotionList<string>.LeaveDuration);

        Assert.Equal(["a:", "b:motion-enter"], Items(cut));
    }

    [Fact]
    public void The_same_list_again_keeps_each_item_animating()
    {
        List<string> items = ["a"];
        var cut = RenderList(items);
        List<string> joined = ["a", "b"];
        SetItems(cut, joined);

        SetItems(cut, joined);

        Assert.Equal(["a:", "b:motion-enter"], Items(cut));
    }

    [Fact]
    public void A_table_body_marks_its_rows_as_rows()
    {
        var cut = Render<MotionList<string>>(parameters => parameters
            .Add(list => list.Items, ["a"])
            .Add(list => list.Key, item => item)
            .Add(list => list.TableBody, true)
            .Add(list => list.ChildContent, entry => builder =>
            {
                builder.OpenElement(0, "tr");
                builder.AddAttribute(1, "class", entry.Class);
                builder.OpenElement(2, "td");
                builder.AddContent(3, entry.Item);
                builder.CloseElement();
                builder.CloseElement();
            }));

        cut.Render(parameters => parameters.Add(list => list.Items, ["a", "b"]));

        Assert.Equal("tbody", cut.Find(".motion-list").TagName, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("motion-row-enter", cut.FindAll("tr")[1].ClassName);
    }

    private IRenderedComponent<MotionList<string>> RenderList(IReadOnlyList<string> items) =>
        Render<MotionList<string>>(parameters => parameters
            .Add(list => list.Items, items)
            .Add(list => list.Key, item => item)
            .Add(list => list.ChildContent, ItemFragment));

    private static void SetItems(IRenderedComponent<MotionList<string>> cut, IReadOnlyList<string> items) =>
        cut.Render(parameters => parameters.Add(list => list.Items, items));

    private static RenderFragment ItemFragment(MotionEntry<string> entry) => builder =>
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"item {entry.Class}".Trim());
        builder.AddContent(2, entry.Item);
        builder.CloseElement();
    };

    /// <summary>Each item's text and its motion class, such as "b:motion-enter".</summary>
    private static List<string> Items(IRenderedComponent<MotionList<string>> cut) =>
        [.. cut.FindAll("div.item").Select(item => $"{item.TextContent}:{string.Join(' ', item.ClassList.Where(name => name != "item"))}")];
}
