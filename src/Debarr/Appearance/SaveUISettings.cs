using FluentResults;
using Wolverine.Persistence.EventSourcing;

namespace Debarr.Appearance;

public sealed record SaveUISettings(UITheme Theme, DateTimeFormats Formats, bool ShowRelativeDates)
{
    /// <summary>The UI settings' stream, which Wolverine loads the aggregate from.</summary>
    public Guid UISettingsId => UISettings.StreamId;
}

public static class SaveUISettingsHandler
{
    /// <summary>Refuses a format outside the ones Settings &gt; UI offers, beneath its field.</summary>
    public static Result Validate(SaveUISettings command, UISettings? settings)
    {
        var errors = new List<IError>();
        if (!DateTimeFormats.ShortDateFormats.Contains(command.Formats.ShortDateFormat))
        {
            errors.Add(new FieldError(nameof(DateTimeFormats.ShortDateFormat), "Choose one of the short date formats."));
        }

        if (!DateTimeFormats.LongDateFormats.Contains(command.Formats.LongDateFormat))
        {
            errors.Add(new FieldError(nameof(DateTimeFormats.LongDateFormat), "Choose one of the long date formats."));
        }

        if (!DateTimeFormats.TimeFormats.Contains(command.Formats.TimeFormat))
        {
            errors.Add(new FieldError(nameof(DateTimeFormats.TimeFormat), "Choose one of the time formats."));
        }

        return errors.Count > 0 ? Result.Fail(errors) : Result.Ok();
    }

    public static UISettingsChanged Handle(SaveUISettings command, [WriteModel(Required = false)] UISettings? settings) =>
        new(command.Theme, command.Formats, command.ShowRelativeDates);
}
