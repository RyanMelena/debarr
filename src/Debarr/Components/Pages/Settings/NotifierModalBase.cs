using Debarr.EventStore;
using Debarr.Notifying;
using Debarr.Playing;
using Fisher;
using FluentResults;
using Microsoft.AspNetCore.Components;
using Wolverine.Runtime;

namespace Debarr.Components.Pages.Settings;

/// <summary>A modal that edits one notifier through its type's form.</summary>
/// <param name="copy">A copy of a form that edits apart from it.</param>
/// <param name="hasChanges">Whether the edited form differs from the saved one; by default, whether the two are unequal.</param>
public abstract class NotifierModalBase<TForm>(Func<TForm, TForm> copy, Func<TForm, TForm, bool>? hasChanges = null) : IntegrationModalBase
    where TForm : class, INotifierForm
{
    private readonly EditedForm<TForm> _form = new(copy, hasChanges);

    [Inject]
    private IDocumentStore Store { get; set; } = default!;

    [Inject]
    private IWolverineRuntime Runtime { get; set; } = default!;

    [Inject]
    private NotificationPublisher NotificationPublisher { get; set; } = default!;

    /// <summary>The notifier as saved when the modal opened, or a new notifier's defaults.</summary>
    [Parameter]
    public TForm Saved { get; set; } = default!;

    protected TForm Form => _form.Model!;

    protected override string RemoveTitle => "Remove Notifier";

    protected override string RemoveMessage => $"Remove the notifier {Form.Name}?";

    /// <summary>The form of a stored notifier; null when its settings are another type's.</summary>
    protected abstract TForm? ToForm(Notifier notifier);

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _form.Take(Saved);
    }

    protected override async Task<Result> ValidateIntegrationAsync(CancellationToken cancellationToken)
    {
        var notifiers = await ReadNotifiersAsync(cancellationToken);
        return Form.ToSaveNotifier(notifiers).Bind(command => SaveNotifierHandler.Validate(command, notifiers));
    }

    protected override async Task<(bool Succeeded, string? Message)> TestIntegrationAsync(CancellationToken cancellationToken)
    {
        var notifier = Form.ToSaveNotifier(Notifiers.Empty).Value;
        var delivery = await NotificationPublisher.TestAsync(notifier.NotifierId, notifier.Name, notifier.Settings, cancellationToken);
        return delivery.Outcome switch
        {
            DeliveryOutcome.Succeeded => (true, $"Delivered in {(int)delivery.Duration.TotalMilliseconds} ms"),
            DeliveryOutcome.Failed failed => (false, failed.Error),
            _ => (false, null),
        };
    }

    protected override async Task<ResultBase> SaveIntegrationAsync(CancellationToken cancellationToken)
    {
        var notifiers = await ReadNotifiersAsync(cancellationToken);
        if (notifiers.Find(Form.Id) is { } notifier && ToForm(notifier) is { } stored)
        {
            _form.Take(stored);
        }

        return await _form.SaveAsync(async form =>
        {
            var command = form.ToSaveNotifier(notifiers);
            return command.IsSuccess ? await Runtime.SendCommandAsync(command.Value, cancellationToken) : command.ToResult();
        });
    }

    protected override async Task<ResultBase> RemoveIntegrationAsync(CancellationToken cancellationToken) =>
        await Runtime.SendCommandAsync(new RemoveNotifier(Form.Id), cancellationToken);

    private async Task<Notifiers> ReadNotifiersAsync(CancellationToken cancellationToken)
    {
        await using var session = Store.QuerySession();
        return await Notifiers.ReadAsync(session, cancellationToken);
    }
}
