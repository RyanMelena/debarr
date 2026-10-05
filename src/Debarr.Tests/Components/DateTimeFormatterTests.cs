using Debarr.Components;
using Debarr.Appearance;
using Microsoft.Extensions.Time.Testing;

namespace Debarr.Tests.Components;

public class DateTimeFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 18, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _timeProvider = new(Now);

    public DateTimeFormatterTests() => _timeProvider.SetLocalTimeZone(TimeZoneInfo.Utc);

    [Theory]
    [InlineData("MMM d yyyy", "Mar 5 2026 17:30")]
    [InlineData("dd MMM yyyy", "05 Mar 2026 17:30")]
    [InlineData("MM/d/yyyy", "03/5/2026 17:30")]
    [InlineData("dd/MM/yyyy", "05/03/2026 17:30")]
    [InlineData("yyyy-MM-dd", "2026-03-05 17:30")]
    public void A_date_follows_the_short_date_format(string shortDateFormat, string expected)
    {
        var formatter = Formatter(Settings(shortDateFormat: shortDateFormat, showRelativeDates: false));

        Assert.Equal(expected, formatter.FormatDateTime(new DateTimeOffset(2026, 3, 5, 17, 30, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData("dddd, MMMM d yyyy", "HH:mm", "Thursday, March 5 2026 17:30")]
    [InlineData("dddd, d MMMM yyyy", "h:mm tt", "Thursday, 5 March 2026 5:30 PM")]
    public void A_long_date_follows_the_long_date_and_time_formats(string longDateFormat, string timeFormat, string expected)
    {
        var formatter = Formatter(Settings(longDateFormat: longDateFormat, timeFormat: timeFormat));

        Assert.Equal(expected, formatter.FormatLongDateTime(new DateTimeOffset(2026, 3, 5, 17, 30, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData("HH:mm", "Mar 5 2026 09:05")]
    [InlineData("h:mm tt", "Mar 5 2026 9:05 AM")]
    public void A_time_follows_the_time_format(string timeFormat, string expected)
    {
        var formatter = Formatter(Settings(timeFormat: timeFormat, showRelativeDates: false));

        Assert.Equal(expected, formatter.FormatDateTime(new DateTimeOffset(2026, 3, 5, 9, 5, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData("HH:mm", "Mar 5 2026 09:05:07")]
    [InlineData("h:mm tt", "Mar 5 2026 9:05:07 AM")]
    public void A_time_with_seconds_adds_them_after_the_minutes_of_the_time_format(string timeFormat, string expected)
    {
        var formatter = Formatter(Settings(timeFormat: timeFormat, showRelativeDates: false));

        Assert.Equal(expected, formatter.FormatDateTime(new DateTimeOffset(2026, 3, 5, 9, 5, 7, TimeSpan.Zero), withSeconds: true));
    }

    [Theory]
    [InlineData(true, "2026-09-27T00:00:00Z", "Today 00:00")]
    [InlineData(true, "2026-09-26T23:59:00Z", "Yesterday 23:59")]
    [InlineData(true, "2026-09-25T23:59:00Z", "Friday 23:59")]
    [InlineData(true, "2026-09-21T08:00:00Z", "Monday 08:00")]
    [InlineData(true, "2026-09-20T08:00:00Z", "Sep 20 08:00")]
    [InlineData(true, "2025-12-31T08:00:00Z", "Dec 31 2025 08:00")]
    [InlineData(false, "2026-09-27T12:00:00Z", "Sep 27 2026 12:00")]
    [InlineData(false, "2026-09-25T12:00:00Z", "Sep 25 2026 12:00")]
    public void Relative_dates_name_the_past_week_and_leave_out_the_current_year_only_when_shown(bool showRelativeDates, string value, string expected)
    {
        var formatter = Formatter(Settings(showRelativeDates: showRelativeDates));

        Assert.Equal(expected, formatter.FormatDateTime(DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("MMM d yyyy", "Mar 5 17:30")]
    [InlineData("dd MMM yyyy", "05 Mar 17:30")]
    [InlineData("MM/d/yyyy", "03/5 17:30")]
    [InlineData("MM/dd/yyyy", "03/05 17:30")]
    [InlineData("dd/MM/yyyy", "05/03 17:30")]
    [InlineData("yyyy-MM-dd", "03-05 17:30")]
    public void A_relative_date_in_the_current_year_leaves_the_year_out_of_every_short_date_format(string shortDateFormat, string expected)
    {
        var formatter = Formatter(Settings(shortDateFormat: shortDateFormat));

        Assert.Equal(expected, formatter.FormatDateTime(new DateTimeOffset(2026, 3, 5, 17, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void A_moment_shows_in_the_local_time_zone_and_today_is_the_local_date()
    {
        // 03:00 UTC on the 28th is 23:00 on the 27th at UTC-4, which is today there.
        _timeProvider.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("UTC-4", TimeSpan.FromHours(-4), "UTC-4", "UTC-4"));
        _timeProvider.SetUtcNow(new DateTimeOffset(2026, 9, 28, 3, 30, 0, TimeSpan.Zero));
        var formatter = Formatter(UISettings.Default);

        Assert.Equal("Today 23:00", formatter.FormatDateTime(new DateTimeOffset(2026, 9, 28, 3, 0, 0, TimeSpan.Zero)));
    }

    private static UISettings Settings(string? shortDateFormat = null, string? longDateFormat = null, string? timeFormat = null, bool showRelativeDates = true) => new(
        UITheme.Auto,
        new DateTimeFormats(
            shortDateFormat ?? DateTimeFormats.Default.ShortDateFormat,
            longDateFormat ?? DateTimeFormats.Default.LongDateFormat,
            timeFormat ?? DateTimeFormats.Default.TimeFormat),
        showRelativeDates);

    private DateTimeFormatter Formatter(UISettings settings) => new(settings, _timeProvider);
}
