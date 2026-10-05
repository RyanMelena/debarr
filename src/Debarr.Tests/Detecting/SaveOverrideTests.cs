using Debarr.Detecting;
using Microsoft.Extensions.Time.Testing;

namespace Debarr.Tests.Detecting;

public sealed class SaveOverrideTests
{
    private static readonly FileHash Hash = TestFileHash.For("arrival");

    private static readonly DateTimeOffset Now = new(2026, 9, 30, 20, 15, 0, TimeSpan.Zero);

    private static readonly VideoFile Discovered = VideoFile.Create(new VideoFileDiscovered(Hash, 42, Now));

    [Fact]
    public void An_override_is_saved_now_with_a_blank_note_as_none()
    {
        var command = new SaveOverride(Hash, 2.39, true, "  ");

        var parsed = SaveOverrideHandler.Validate(command, Discovered);

        Assert.True(parsed.IsSuccess);
        Assert.Equal(new OverrideSaved(new Override(new AspectRatio(2.39), true, null, Now)), SaveOverrideHandler.Handle(command, Discovered, parsed.Value, new FakeTimeProvider(Now)));
    }

    [Fact]
    public void An_override_of_a_missing_video_file_or_with_a_ratio_of_0_is_refused()
    {
        Assert.Equal("The video file no longer exists.", Assert.Single(SaveOverrideHandler.Validate(new SaveOverride(Hash, 2.39, false, null), null).Errors).Message);
        Assert.IsType<FieldError>(Assert.Single(SaveOverrideHandler.Validate(new SaveOverride(Hash, 0, false, null), Discovered).Errors));
    }

    [Fact]
    public void An_override_of_an_archived_video_file_is_refused()
    {
        Assert.Equal(
            "The video file is archived.",
            Assert.Single(SaveOverrideHandler.Validate(new SaveOverride(Hash, 2.39, false, null), Discovered.Apply(new VideoFileArchived(Now))).Errors).Message);
    }
}
