using MudBlazor;

namespace Debarr.Components;

/// <summary>How the UI shows one state: its word, its icon and its colour.</summary>
public sealed record StateDisplay(string Text, string Icon, Color Color);
