using Debarr.Activity;
using Debarr.Hosting;
using FluentResults;
using MudBlazor;

namespace Debarr.Components.Pages.Settings;

public partial class GeneralPage(IConfiguration configuration, HostSettingsFile hostSettingsFile)
{
    private readonly EditedForm<GeneralSettingsForm> _form = new(model => model.Copy(), (edited, saved) => edited.GetChanges(saved).Count > 0);
    private MudForm? _mudForm;
    private GeneralSettingsForm? _running;
    private readonly PageAction _save = new();

    protected override bool ShowsActivity(ActivityEvent activityEvent) => activityEvent is HostSettingsSavedEvent;

    protected override Task ReloadAsync(CancellationToken cancellationToken)
    {
        _running = GeneralSettingsForm.FromConfiguration(configuration);
        _form.Take(GeneralSettingsForm.FromConfiguration(hostSettingsFile.BuildSavedConfiguration(configuration)));
        return Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        if (_mudForm is null || _form is not { Model: { } model, Saved: { } saved })
        {
            return;
        }

        await _mudForm.ValidateAsync();
        if (!_mudForm.IsValid)
        {
            return;
        }

        var changes = model.GetChanges(saved)
            .Where(change => EnvironmentOverrides.FindVariable(change.Key) is null)
            .ToDictionary(change => change.Key, change => change.Value);
        await _save.RunAsync(() => _form.SaveAsync(async _ =>
        {
            await hostSettingsFile.SaveAsync(changes, CancellationToken.None);
            return Result.Ok();
        }), Logger, "The settings were not saved.");
    }
}
