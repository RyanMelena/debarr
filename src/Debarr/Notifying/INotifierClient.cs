using Debarr.Playing;
using FluentResults;

namespace Debarr.Notifying;

/// <summary>Sends notifications to the destination one notifier names.</summary>
public interface INotifierClient
{
    /// <summary>
    /// Sends the notification once. The caller owns the timeout.
    /// Every failure is a failed result, such as an unusable address, a refused connection or a rejection from the destination.
    /// Once the token is cancelled, the send throws.
    /// </summary>
    Task<Result> SendAsync(Notification notification, CancellationToken cancellationToken);
}
