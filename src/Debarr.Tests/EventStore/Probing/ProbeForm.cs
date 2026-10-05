using Debarr.EventStore;
using Debarr.Extensions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Wolverine.Runtime;

namespace Debarr.Tests.EventStore.Probing;

/// <summary>A form that renames a probe as a page would: its field's error beneath it, and its form error above Save.</summary>
public sealed class ProbeForm : ComponentBase
{
    private string _name = "";
    private Dictionary<string, string> _fieldErrors = [];
    private string? _formError;

    [Inject]
    public required IWolverineRuntime Runtime { get; set; }

    [Parameter]
    public Guid ProbeId { get; set; }

    [Parameter]
    public long Version { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "input");
        builder.AddAttribute(1, "id", "probe-name");
        builder.AddAttribute(2, "value", _name);
        builder.AddAttribute(3, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, changed => _name = (string)changed.Value!));
        builder.CloseElement();

        if (_fieldErrors.TryGetValue(nameof(RenameProbe.Name), out var fieldError))
        {
            builder.OpenElement(4, "p");
            builder.AddAttribute(5, "id", "probe-name-error");
            builder.AddContent(6, fieldError);
            builder.CloseElement();
        }

        if (_formError is not null)
        {
            builder.OpenElement(7, "p");
            builder.AddAttribute(8, "id", "probe-form-error");
            builder.AddContent(9, _formError);
            builder.CloseElement();
        }

        builder.OpenElement(10, "button");
        builder.AddAttribute(11, "id", "probe-save");
        builder.AddAttribute(12, "onclick", EventCallback.Factory.Create(this, SaveAsync));
        builder.AddContent(13, "Save");
        builder.CloseElement();
    }

    private async Task SaveAsync()
    {
        var result = await Runtime.SendCommandAsync(new RenameProbe(ProbeId, _name, Version), CancellationToken.None);
        _fieldErrors = result.GetFieldErrors();
        _formError = result.GetFormError();
    }
}
