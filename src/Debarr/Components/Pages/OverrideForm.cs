using Debarr.Detecting;

namespace Debarr.Components.Pages;

/// <summary>A video file's override as the video file detail page edits it.</summary>
public sealed record OverrideForm
{
    /// <summary>The ratio to send; null to send the current result or the player's ratio.</summary>
    public double? AspectRatio { get; set; }

    public bool DontSend { get; set; }

    public string? Note { get; set; }

    /// <summary>The saved override as the form shows it; an empty form before the first save.</summary>
    public static OverrideForm From(Override? @override) => new()
    {
        AspectRatio = @override?.AspectRatio?.Value,
        DontSend = @override?.DontSend ?? false,
        Note = @override?.Note,
    };

    /// <summary>The form as it is saved: a blank note becomes null, and a note keeps no surrounding white space.</summary>
    public OverrideForm Normalized() => this with { Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim() };
}
