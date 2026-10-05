namespace Debarr.Components;

/// <summary>A filter's chip: its query string value, its label, and how many rows it matches among those the other filters and the search match.</summary>
public sealed record FilterChip(string Value, string Label, int Count);
