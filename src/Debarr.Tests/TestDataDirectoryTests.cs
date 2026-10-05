namespace Debarr.Tests;

public sealed class TestDataDirectoryTests
{
    [Fact]
    public async Task A_directory_whose_file_closes_after_the_finalizers_run_is_deleted_once_it_closes()
    {
        var path = TestDataDirectory.Create();
        var held = new FileStream(Path.Combine(path, "debarr.db"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        _ = Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken).ContinueWith(_ => held.Dispose(), TaskScheduler.Default);

        await TestDataDirectory.DeleteAsync(path);

        Assert.False(Directory.Exists(path));
    }
}
