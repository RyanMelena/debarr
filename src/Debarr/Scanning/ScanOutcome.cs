namespace Debarr.Scanning;

/// <summary>How a library or folder scan that returned ended: at its end, or cancelled by a scan pause, after which its job runs it again.</summary>
public enum ScanOutcome
{
    Finished,
    Cancelled,
}
