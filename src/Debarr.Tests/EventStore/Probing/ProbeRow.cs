using Fisher.Projections;

namespace Debarr.Tests.EventStore.Probing;

/// <summary>The probe's read model: one document per probe with its name.</summary>
public sealed class ProbeRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";
}

public sealed class ProbeRowProjection : SingleStreamProjection<ProbeRow, Guid>
{
    public const string ReadModel = nameof(ProbeRow);

    public ProbeRowProjection() => Name = ReadModel;

    public static ProbeRow Create(ProbeStarted started) => new() { Name = started.Name };

    public static void Apply(ProbeRenamed renamed, ProbeRow row) => row.Name = renamed.Name;
}
