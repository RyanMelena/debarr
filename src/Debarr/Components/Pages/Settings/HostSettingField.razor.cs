using System.Linq.Expressions;
using Debarr.Hosting;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Debarr.Components.Pages.Settings;

/// <summary>
/// The field that edits one host setting: a text field, or a select of the <see cref="ChildContent"/> items.
/// While an environment variable sets the key, the field is locked, shows a lock in a dashed, shaded box, and its help names the variable.
/// </summary>
public partial class HostSettingField<T>
{
    private string? _variable;

    /// <summary>The setting's configuration key, such as Server:Port.</summary>
    [Parameter, EditorRequired]
    public string Key { get; set; } = "";

    [Parameter, EditorRequired]
    public string Label { get; set; } = "";

    [Parameter]
    public string? Help { get; set; }

    [Parameter]
    public T? Value { get; set; }

    [Parameter]
    public EventCallback<T> ValueChanged { get; set; }

    /// <summary>The bound value, through which the form checks its annotations.</summary>
    [Parameter]
    public Expression<Func<T>>? ValueExpression { get; set; }

    [Parameter]
    public InputType InputType { get; set; } = InputType.Text;

    /// <summary>The select's items; null for a text field.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private bool IsSetByVariable => _variable is not null;

    private string? SetByVariableClass => IsSetByVariable ? "set-by-variable" : null;

    private string? HelperText => (_variable, Help) switch
    {
        ({ } variable, { } help) => $"Set by {variable}. {help}",
        ({ } variable, null) => $"Set by {variable}",
        (null, var help) => help,
    };

    protected override void OnInitialized() => _variable = EnvironmentOverrides.FindVariable(Key);
}
