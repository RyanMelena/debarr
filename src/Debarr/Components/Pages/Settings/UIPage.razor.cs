using System.Globalization;
using Debarr.Appearance;
using Debarr.EventStore;
using Fisher;
using Wolverine.Runtime;

namespace Debarr.Components.Pages.Settings;

public partial class UIPage(IDocumentStore store, IWolverineRuntime runtime)
{
    // Each format is shown as this moment.
    private static readonly DateTime ExampleMoment = new(2014, 3, 25, 17, 30, 0);

    private readonly EditedForm<UISettingsForm> _form = new(model => model with { });
    private readonly PageAction _save = new();

    protected override IReadOnlySet<string> ReadModels { get; } = new HashSet<string> { nameof(UISettings) };

    protected override async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        _form.Take(UISettingsForm.FromSettings(await UISettings.ReadAsync(session, cancellationToken)));
    }

    private static string Example(string format) => ExampleMoment.ToString(format, CultureInfo.InvariantCulture);

    private Task SaveAsync() =>
        _save.RunAsync(() => _form.SaveAsync(model => runtime.SendCommandAsync(model.ToSaveUISettings(), CancellationToken.None)), Logger, "The settings were not saved.");
}
