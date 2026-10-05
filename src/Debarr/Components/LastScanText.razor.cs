using Debarr.Scanning;
using Microsoft.AspNetCore.Components;

namespace Debarr.Components;

/// <summary>When a scan last ran, marked when it failed, was cancelled or was interrupted, or Never.</summary>
public partial class LastScanText
{
    /// <summary>When the scan started and how it ended; null when none has run.</summary>
    [Parameter]
    public (DateTimeOffset StartedAt, LibraryScanOutcome Outcome)? Scan { get; set; }

    /// <summary>A library scan's start and outcome; null while none has closed.</summary>
    public static (DateTimeOffset StartedAt, LibraryScanOutcome Outcome)? Of(LibraryScanSummaryRow? scan) =>
        scan is { Outcome: { } outcome } ? (scan.StartedAt, outcome) : null;

    /// <summary>A root folder's scan, which finished when it read the root folder and failed otherwise.</summary>
    public static (DateTimeOffset StartedAt, LibraryScanOutcome Outcome)? Of(RootFolderScanned? scan) =>
        scan is null ? null : (scan.StartedAt, scan.Error is { } error ? new LibraryScanOutcome.Failed(error) : new LibraryScanOutcome.Finished());
}
