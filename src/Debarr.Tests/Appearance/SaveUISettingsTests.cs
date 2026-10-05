using Debarr.Appearance;

namespace Debarr.Tests.Appearance;

public sealed class SaveUISettingsTests
{
    [Fact]
    public void A_save_changes_every_setting()
    {
        var command = new SaveUISettings(UITheme.Dark, new DateTimeFormats("yyyy-MM-dd", "dddd, d MMMM yyyy", "h:mm tt"), false);

        Assert.True(SaveUISettingsHandler.Validate(command, UISettings.Default).IsSuccess);
        Assert.Equal(
            new UISettingsChanged(UITheme.Dark, new DateTimeFormats("yyyy-MM-dd", "dddd, d MMMM yyyy", "h:mm tt"), false),
            SaveUISettingsHandler.Handle(command, UISettings.Default));
    }

    [Fact]
    public void A_save_before_the_first_one_validates_and_decides_against_the_defaults()
    {
        var command = new SaveUISettings(UITheme.Light, DateTimeFormats.Default, false);

        Assert.True(SaveUISettingsHandler.Validate(command, null).IsSuccess);
        Assert.Equal(new UISettingsChanged(UITheme.Light, DateTimeFormats.Default, false), SaveUISettingsHandler.Handle(command, null));
        Assert.Equal(new UISettings(UITheme.Light, DateTimeFormats.Default, false), UISettings.Create(SaveUISettingsHandler.Handle(command, null)));
    }

    [Fact]
    public void Every_offered_format_is_accepted()
    {
        var commands =
            from shortDate in DateTimeFormats.ShortDateFormats
            from longDate in DateTimeFormats.LongDateFormats
            from time in DateTimeFormats.TimeFormats
            select new SaveUISettings(UITheme.Auto, new DateTimeFormats(shortDate, longDate, time), true);

        Assert.All(commands, command => Assert.True(SaveUISettingsHandler.Validate(command, UISettings.Default).IsSuccess));
    }

    [Fact]
    public void A_format_outside_the_offered_ones_is_refused_beneath_its_field()
    {
        var command = new SaveUISettings(UITheme.Auto, new DateTimeFormats("d.M.yy", "dddd, MMMM d yyyy", "HH:mm:ss"), true);

        var errors = SaveUISettingsHandler.Validate(command, UISettings.Default).Errors.OfType<FieldError>().Select(error => error.Field);

        Assert.Equal([nameof(DateTimeFormats.ShortDateFormat), nameof(DateTimeFormats.TimeFormat)], errors);
    }

    [Fact]
    public void The_settings_take_the_saved_values()
    {
        var changed = new UISettingsChanged(UITheme.Light, DateTimeFormats.Default with { TimeFormat = "h:mm tt" }, false);

        Assert.Equal(new UISettings(UITheme.Light, DateTimeFormats.Default with { TimeFormat = "h:mm tt" }, false), UISettings.Default.Apply(changed));
    }

    [Fact]
    public void The_defaults_are_offered_formats()
    {
        Assert.Contains(UISettings.Default.Formats.ShortDateFormat, DateTimeFormats.ShortDateFormats);
        Assert.Contains(UISettings.Default.Formats.LongDateFormat, DateTimeFormats.LongDateFormats);
        Assert.Contains(UISettings.Default.Formats.TimeFormat, DateTimeFormats.TimeFormats);
    }
}
