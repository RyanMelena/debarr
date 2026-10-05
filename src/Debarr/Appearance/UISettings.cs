using Fisher;

namespace Debarr.Appearance;

/// <summary>How the UI shows things: the theme, the date and time formats, and whether recent dates show as relative.</summary>
/// <param name="ShowRelativeDates">Shows a date today or yesterday as Today or Yesterday.</param>
public sealed record UISettings(UITheme Theme, DateTimeFormats Formats, bool ShowRelativeDates)
{
    /// <summary>The one stream the UI settings' events are appended to.</summary>
    public static readonly Guid StreamId = new("724eee22-7025-487f-87da-e6c1e935fd22");

    /// <summary>The settings before the first save, which a missing stream folds to.</summary>
    public static UISettings Default { get; } = new(UITheme.Auto, DateTimeFormats.Default, true);

    public static async Task<UISettings> ReadAsync(IQuerySession session, CancellationToken cancellationToken) =>
        await session.Events.FetchLatest<UISettings>(StreamId, cancellationToken) ?? Default;

    /// <summary>The stream's id, which the event store keys the aggregate on.</summary>
    public Guid Id => StreamId;

    public static UISettings Create(UISettingsChanged changed) => Default.Apply(changed);

    public UISettings Apply(UISettingsChanged changed) => new(changed.Theme, changed.Formats, changed.ShowRelativeDates);
}

/// <summary>The .NET format strings the UI shows dates and times with, formatted with the invariant culture.</summary>
public sealed record DateTimeFormats(string ShortDateFormat, string LongDateFormat, string TimeFormat)
{
    public static IReadOnlyList<string> ShortDateFormats { get; } = ["MMM d yyyy", "dd MMM yyyy", "MM/d/yyyy", "MM/dd/yyyy", "dd/MM/yyyy", "yyyy-MM-dd"];

    public static IReadOnlyList<string> LongDateFormats { get; } = ["dddd, MMMM d yyyy", "dddd, d MMMM yyyy"];

    public static IReadOnlyList<string> TimeFormats { get; } = ["h:mm tt", "HH:mm"];

    public static DateTimeFormats Default { get; } = new("MMM d yyyy", "dddd, MMMM d yyyy", "HH:mm");
}
