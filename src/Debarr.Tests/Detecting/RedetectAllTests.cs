using Debarr.Detecting;
using Debarr.EventStore;
using Microsoft.Extensions.Time.Testing;
using Wolverine.Fisher;

namespace Debarr.Tests.Detecting;

public sealed class RedetectAllTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 20, 15, 0, TimeSpan.Zero);

    [Fact]
    public void Re_detect_all_clears_each_video_file_now_at_the_version_its_row_was_read_at()
    {
        StreamVersion[] videoFiles = [new(Guid.CreateVersion7(), 3), new(Guid.CreateVersion7(), 7)];

        var appends = RedetectAllHandler.Handle(new RedetectAll(), videoFiles, new FakeTimeProvider(Now)).Cast<AppendToStream>().ToList();

        Assert.Equal(videoFiles, appends.Select(append => new StreamVersion(append.StreamId, append.ExpectedVersion!.Value)));
        Assert.All(appends, append => Assert.Equal([new DetectionResultCleared(Now)], append.Events));
    }

    [Fact]
    public void Re_detect_all_of_a_library_with_no_results_or_failures_appends_nothing()
    {
        Assert.Empty(RedetectAllHandler.Handle(new RedetectAll(), [], new FakeTimeProvider(Now)));
    }
}
