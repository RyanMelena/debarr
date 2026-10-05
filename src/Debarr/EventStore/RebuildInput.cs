using JasperFx.CommandLine;

namespace Debarr.EventStore;

public sealed class RebuildInput : NetCoreInput
{
    [Description("The read model to rebuild; every read model when left out")]
    public string? ReadModel { get; set; }
}
