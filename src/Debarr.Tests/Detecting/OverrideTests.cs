using Debarr.Detecting;
using Debarr.Extensions;

namespace Debarr.Tests.Detecting;

public class OverrideTests
{
    private static readonly DateTimeOffset SavedAt = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_refuses_a_ratio_of_0_or_less_beneath_its_field(double aspectRatio) =>
        Assert.Equal(
            new Dictionary<string, string> { ["AspectRatio"] = "The ratio must be greater than 0." },
            Override.Create(aspectRatio, false, null, SavedAt).GetFieldErrors());

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("  Open matte  ", "Open matte")]
    public void A_blank_note_is_none_and_a_note_is_trimmed(string? note, string? expected) =>
        Assert.Equal(expected, Override.Create(null, false, note, SavedAt).Value.Note);

    [Fact]
    public void Create_keeps_the_ratio_the_flag_and_when_it_was_saved() =>
        Assert.Equal(new Override(new AspectRatio(2.2), true, null, SavedAt), Override.Create(2.2, true, null, SavedAt).Value);

    [Theory]
    [InlineData(null, false, false)]
    [InlineData(2.2, false, true)]
    [InlineData(null, true, true)]
    [InlineData(2.2, true, true)]
    public void An_override_is_manual_when_it_has_a_ratio_or_dont_send(double? aspectRatio, bool dontSend, bool isManual) =>
        Assert.Equal(isManual, Override.Create(aspectRatio, dontSend, "note", SavedAt).Value.IsManual);
}
