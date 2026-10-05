using Microsoft.AspNetCore.Components;

namespace Debarr.Components;

/// <summary>Placeholder rows for a paged table's first read, one for each row the read is expected to bring, so the rows land where the placeholders were.</summary>
public partial class TableSkeleton
{
    [Parameter, EditorRequired]
    public int Rows { get; set; }

    [Parameter, EditorRequired]
    public int Columns { get; set; }

    /// <summary>Whether each cell holds two lines of text, as a row with a secondary line beneath its values does.</summary>
    [Parameter]
    public bool TwoLines { get; set; }
}
