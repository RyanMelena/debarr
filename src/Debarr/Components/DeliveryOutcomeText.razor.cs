using Debarr.Playing;
using Microsoft.AspNetCore.Components;

namespace Debarr.Components;

/// <summary>How one delivery ended, such as "Delivered in 32 ms" or "Failed: Connection refused".</summary>
public partial class DeliveryOutcomeText
{
    [Parameter, EditorRequired]
    public Delivery Delivery { get; set; } = default!;
}
