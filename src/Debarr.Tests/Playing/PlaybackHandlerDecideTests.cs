using Debarr.Detecting;
using Debarr.Playing;
using Debarr.Tests.Detecting;

namespace Debarr.Tests.Playing;

public sealed class PlaybackHandlerDecideTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 20, 0, 0, TimeSpan.Zero);

    private static readonly Guid DetectionId = new("0199a3c4-5b6e-7000-8000-000000000076");

    private static readonly Detection DetectedResult = TestVideoFile.Detected(2.3964, AspectRatioSource.Detected) with { Id = DetectionId };

    private static readonly Detection FromFileResult = TestVideoFile.Detected(1.8519, AspectRatioSource.FromFile) with { Id = DetectionId };

    private static readonly Override NoteOnly = new(null, false, "Theatrical cut.", Now);

    private static readonly Override ManualRatio = new(new AspectRatio(1.8), false, null, Now);

    private static readonly Override DontSendWithRatio = new(new AspectRatio(2.76), true, null, Now);

    public static TheoryData<string, Override?, Detection?, double?, PlaybackOutcome> Rules => new()
    {
        { "2. no video file sends the snapped player ratio", null, null, 2.3975, Sent(2.39, NotificationAspectRatioSource.Player) },
        { "2. no video file and no player ratio sends nothing", null, null, null, new PlaybackOutcome.NoPlayerAspectRatio() },
        { "3. don't send wins over the override's ratio and the result", DontSendWithRatio, DetectedResult, 1.7778, new PlaybackOutcome.DontSend() },
        { "3. don't send with no player ratio", DontSendWithRatio, null, null, new PlaybackOutcome.DontSend() },
        { "4. the override's ratio is sent as set over the result", ManualRatio, DetectedResult, 1.7778, Sent(1.8, NotificationAspectRatioSource.Manual) },
        { "4. the override's ratio is sent with no player ratio", ManualRatio, null, null, Sent(1.8, NotificationAspectRatioSource.Manual) },
        { "5. a detected result sends its snapped ratio and detection", null, DetectedResult, 1.7778, Sent(2.39, NotificationAspectRatioSource.Detected, DetectionId) },
        { "5. a result from file sends its snapped ratio as container", null, FromFileResult, 1.7778, Sent(1.85, NotificationAspectRatioSource.Container, DetectionId) },
        { "5. an override with only a note leaves the result", NoteOnly, DetectedResult, 1.7778, Sent(2.39, NotificationAspectRatioSource.Detected, DetectionId) },
        { "5. a result is sent with no player ratio", null, DetectedResult, null, Sent(2.39, NotificationAspectRatioSource.Detected, DetectionId) },
        { "6. no result sends the snapped player ratio", NoteOnly, null, 2.3975, Sent(2.39, NotificationAspectRatioSource.Player) },
        { "6. no result and no player ratio sends nothing", NoteOnly, null, null, new PlaybackOutcome.NoPlayerAspectRatio() },
    };

    [Theory]
    [MemberData(nameof(Rules))]
    public void A_playback_of_a_file_sends_what_the_first_rule_that_applies_chooses(
        string rule,
        Override? @override,
        Detection? currentResult,
        double? playerAspectRatio,
        PlaybackOutcome expected)
    {
        var outcome = PlaybackHandler.Decide(@override, currentResult, playerAspectRatio is { } ratio ? new AspectRatio(ratio) : null, StandardRatios.Default);

        Assert.Equal((rule, expected), (rule, outcome));
    }

    private static PlaybackOutcome Sent(double aspectRatio, NotificationAspectRatioSource source, Guid? detectionId = null) =>
        new PlaybackOutcome.Sent(aspectRatio, source, detectionId);
}
