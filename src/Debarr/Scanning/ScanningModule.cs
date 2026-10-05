using Debarr.Activity;
using Debarr.EventStore;
using Debarr.Health;
using Fisher;
using Quartz;

namespace Debarr.Scanning;

public static class ScanningModule
{
    public static IServiceCollection AddScanning(this IServiceCollection services)
    {
        services.AddSingleton<LibraryScanner>();
        services.AddSingleton<IActivitySource>(provider => provider.GetRequiredService<LibraryScanner>());
        services.AddSingleton<RootFolderRemover>();
        services.AddSingleton<IActivitySource>(provider => provider.GetRequiredService<RootFolderRemover>());
        services.AddSingleton<FolderWatcher>();
        services.AddSingleton<IHealthCheck, RootFolderHealthCheck>();
        services.AddFoldedAggregate<Library>();
        return services.ConfigureFisher(options =>
        {
            LibraryScanSummaryRowProjection.AddTo(options);
            StoredFilePathProjection.AddTo(options);
        });
    }

    /// <summary>Adds the library and folder scan jobs and the limit that runs one scan at a time.</summary>
    public static IQuartzBuilder AddScanJobs(this IQuartzBuilder quartz)
    {
        quartz.AddJob<LibraryScanJob>(job => job.WithIdentity(LibraryScanJob.Key).StoreDurably());
        quartz.AddJob<FolderScanJob>(job => job.WithIdentity(FolderScanJob.Key).StoreDurably());
        quartz.UseExecutionLimits(limits => limits.ForGroup(LibraryScanner.ExecutionGroup, 1));

        // Quartz frees the group's slot only after a finished job wakes the scheduler,
        // so the idle wait bounds how long a held scan waits after the one before it ends.
        quartz.ConfigureScheduler(options => options.IdleWaitTime = TimeSpan.FromSeconds(1));

        return quartz;
    }
}
