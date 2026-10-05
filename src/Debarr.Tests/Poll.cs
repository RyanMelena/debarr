namespace Debarr.Tests;

/// <summary>Waits for a condition that work on another thread makes true.</summary>
public static class Poll
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(20);

    public static Task UntilAsync(Func<bool> condition, TimeSpan? timeout = null) =>
        UntilAsync(() => Task.FromResult(condition()), timeout);

    public static async Task UntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = TimeProvider.System.GetUtcNow() + (timeout ?? DefaultTimeout);
        while (!await condition())
        {
            if (TimeProvider.System.GetUtcNow() > deadline)
            {
                Assert.Fail($"The condition stayed false for {timeout ?? DefaultTimeout}.");
            }

            await Task.Delay(Interval, TestContext.Current.CancellationToken);
        }
    }
}
