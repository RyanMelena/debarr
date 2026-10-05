using Microsoft.AspNetCore.Components;

namespace Debarr.Components;

/// <summary>A moment as a short date and time, with the long form on hover.</summary>
public partial class DateTimeText
{
    [CascadingParameter]
    private DateTimeFormatter Formatter { get; set; } = default!;

    [Parameter, EditorRequired]
    public DateTimeOffset Value { get; set; }

    /// <summary>Whether the time shows seconds, as a log entry's does.</summary>
    [Parameter]
    public bool WithSeconds { get; set; }
}
