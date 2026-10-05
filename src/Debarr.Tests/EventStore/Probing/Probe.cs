namespace Debarr.Tests.EventStore.Probing;

/// <summary>An aggregate the tests send commands to, with a name its read model shows.</summary>
public sealed class Probe
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public static Probe Create(ProbeStarted started) => new() { Name = started.Name };

    public void Apply(ProbeRenamed renamed) => Name = renamed.Name;
}
