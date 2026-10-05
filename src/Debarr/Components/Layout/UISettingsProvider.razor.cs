using Debarr.Appearance;
using Fisher;
using Microsoft.AspNetCore.Components;

namespace Debarr.Components.Layout;

/// <summary>
/// Reads the UI settings, and again after each save, and cascades them to its content:
/// the <see cref="UITheme"/> to the layout, and a <see cref="DateTimeFormatter"/> to every page.
/// A save cascades new values, which re-renders every component that takes them.
/// </summary>
public partial class UISettingsProvider(IDocumentStore store, TimeProvider timeProvider)
{
    private UITheme _theme;
    private DateTimeFormatter? _formatter;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override IReadOnlySet<string> ReadModels { get; } = new HashSet<string> { nameof(UISettings) };

    protected override async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        var settings = await UISettings.ReadAsync(session, cancellationToken);
        _theme = settings.Theme;
        _formatter = new DateTimeFormatter(settings, timeProvider);
    }
}
