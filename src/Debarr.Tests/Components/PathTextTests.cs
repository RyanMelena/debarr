using System.Net;
using System.Text.RegularExpressions;
using Bunit;
using Debarr.Components;

namespace Debarr.Tests.Components;

public sealed class PathTextTests : BunitContext
{
    [Theory]
    [InlineData(@"\\nas\media\Broken Rip (2019).mkv", new[] { @"\", @"\", @"nas\", @"media\", "Broken Rip (2019).", "mkv" })]
    [InlineData("smb://nas/media/film.mkv", new[] { "smb:/", "/", "nas/", "media/", "film.", "mkv" })]
    [InlineData("JobFactory set to Quartz.Impl.Factory.", new[] { "JobFactory set to Quartz.", "Impl.", "Factory." })]
    [InlineData("Retrying in 1.5 s.", new[] { "Retrying in 1.5 s." })]
    public void A_path_wraps_only_after_a_separator_or_a_dot_between_names(string value, string[] lines)
    {
        var cut = Render<PathText>(parameters => parameters.Add(text => text.Value, value));

        var span = cut.Find(".path-text");
        Assert.Equal(value, span.TextContent);
        Assert.Equal(lines, Regex.Split(span.InnerHtml, "<wbr[^>]*>").Where(line => line.Length > 0).Select(WebUtility.HtmlDecode));
    }
}
