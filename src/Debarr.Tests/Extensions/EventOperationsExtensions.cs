using Fisher.Events;

namespace Debarr.Tests.Extensions;

public static class EventOperationsExtensions
{
    /// <summary>
    /// Appends to the stream at its current version, 0 for a stream not yet started,
    /// as a test arranges its events before the code under test runs.
    /// </summary>
    public static async Task AppendAtCurrentVersionAsync(this EventOperations operations, Guid streamId, params object[] events)
    {
        var state = await operations.FetchStreamStateAsync(streamId, TestContext.Current.CancellationToken);
        operations.Append(streamId, state?.Version ?? 0, events);
    }
}
