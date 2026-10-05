using Debarr.Playing;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Debarr.Components;

/// <summary>A player connection's state, with when it entered it and when a disconnected one retries.</summary>
public partial class PlayerConnectionStateText
{
    /// <summary>Null when the player has no connection, such as while the player loads.</summary>
    [Parameter]
    public PlayerConnectionState? State { get; set; }

    public static Severity SeverityOf(PlayerConnectionState state) => state switch
    {
        PlayerConnectionState.Connected => Severity.Success,
        PlayerConnectionState.Disconnected => Severity.Error,
        PlayerConnectionState.Connecting => Severity.Info,
        _ => Severity.Normal,
    };
}
