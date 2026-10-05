namespace Debarr.Detecting;

/// <summary>A detection pause, which ends when it is disposed. Dispose ends it once, however often it is called.</summary>
public sealed class DetectionPause(Action end) : IDisposable
{
    private Action? _end = end;

    public void Dispose() => Interlocked.Exchange(ref _end, null)?.Invoke();
}
