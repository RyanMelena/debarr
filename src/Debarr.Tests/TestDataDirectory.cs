namespace Debarr.Tests;

/// <summary>Creates a test's data directory, and deletes it once the host that used it has stopped.</summary>
public static class TestDataDirectory
{
    private const string Prefix = "debarr-";
    private static readonly TimeSpan FinalizeAfter = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(20);

    // The process's one finalizer thread closes every test's connections in turn, so its backlog outlasts 30 seconds under a loaded suite.
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    // Older than a directory any test still uses, in this run or in another running at the same time.
    private static readonly TimeSpan LeftBehindAfter = TimeSpan.FromHours(1);

    static TestDataDirectory() => DeleteLeftBehind();

    public static string Create() => Directory.CreateTempSubdirectory(Prefix).FullName;

    /// <summary>
    /// Deletes <paramref name="path"/>, retrying while a file in it is still closing.
    /// A closed connection keeps debarr.db open until the statements of its undisposed commands are finalized,
    /// so after a second of retries the finalizers run before the next attempt.
    /// Past the timeout the last attempt's exception, which names the open file, fails the test.
    /// </summary>
    public static async Task DeleteAsync(string path)
    {
        var started = TimeProvider.System.GetUtcNow();
        var finalized = false;
        while (true)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (IOException) when (TimeProvider.System.GetUtcNow() < started + Timeout)
            {
            }

            if (!finalized && TimeProvider.System.GetUtcNow() > started + FinalizeAfter)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                finalized = true;
                continue;
            }

            await Task.Delay(Interval);
        }
    }

    /// <summary>Deletes the directories that an earlier run's exit left.</summary>
    private static void DeleteLeftBehind()
    {
        foreach (var directory in new DirectoryInfo(Path.GetTempPath()).EnumerateDirectories(Prefix + "*"))
        {
            if (directory.CreationTimeUtc < DateTime.UtcNow - LeftBehindAfter)
            {
                try
                {
                    directory.Delete(recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
