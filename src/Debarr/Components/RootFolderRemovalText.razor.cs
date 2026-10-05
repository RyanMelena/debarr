using Microsoft.AspNetCore.Components;

namespace Debarr.Components;

/// <summary>A root folder whose removal runs, and how long it has run.</summary>
public partial class RootFolderRemovalText
{
    [Parameter, EditorRequired]
    public DateTimeOffset StartedAt { get; set; }
}
