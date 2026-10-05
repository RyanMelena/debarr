namespace Debarr.Scanning;

/// <summary>A scan pause, which ends when it is disposed. Dispose ends it once, however often it is called.</summary>
public sealed class ScanPause(Action end) : IDisposable
{
    private Action? _end = end;

    public void Dispose() => Interlocked.Exchange(ref _end, null)?.Invoke();
}
