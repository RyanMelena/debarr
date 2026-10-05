using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace Debarr.Components;

/// <summary>
/// A path, URL or log message that wraps after a path separator or a dot between names, such as Quartz.Impl, rather than inside a name.
/// A name longer than the line breaks where it must, so the text stays inside its container.
/// </summary>
public partial class PathText
{
    [Parameter, EditorRequired]
    public string? Value { get; set; }

    private string[] Segments => string.IsNullOrEmpty(Value) ? [] : BreakPoints().Split(Value);

    [GeneratedRegex(@"(?<=[\\/])|(?<=\.)(?=\p{L})")]
    private static partial Regex BreakPoints();
}
