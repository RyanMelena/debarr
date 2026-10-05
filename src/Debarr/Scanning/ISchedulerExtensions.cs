using System.Reactive.Disposables;
using System.Reactive.Linq;
using Quartz;

namespace Debarr.Scanning;

public static class ISchedulerExtensions
{
    /// <summary>The starts and ends of the jobs the matcher selects, through a job listener that each subscription adds and removes.</summary>
    public static IObservable<JobEvent> ObserveJobs(this IScheduler scheduler, IMatcher<JobKey> matcher) =>
        Observable.Create<JobEvent>(observer =>
            {
                var listener = new ForwardingJobListener(Guid.NewGuid().ToString(), observer);
                scheduler.ListenerManager.AddJobListener(listener, [matcher]);
                return Disposable.Create(() => scheduler.ListenerManager.RemoveJobListener(listener.Name));
            })
            // Jobs start and end on several scheduler threads at once.
            .Synchronize();

    /// <summary>Each scheduling and unscheduling of the triggers the matcher selects, through a scheduler listener that each subscription adds and removes.</summary>
    public static IObservable<ScheduleChanged> ObserveSchedule(this IScheduler scheduler, IMatcher<TriggerKey> matcher) =>
        Observable.Create<ScheduleChanged>(observer =>
            {
                var listener = new ForwardingSchedulerListener(Guid.NewGuid().ToString(), matcher, observer);
                scheduler.ListenerManager.AddSchedulerListener(listener);
                return Disposable.Create(() => scheduler.ListenerManager.RemoveSchedulerListener(listener.Name));
            })
            // Callers on several threads schedule and unschedule triggers at once.
            .Synchronize();

    /// <summary>Forwards each start and end of a job to an observer.</summary>
    private sealed class ForwardingJobListener(string name, IObserver<JobEvent> observer) : IJobListener
    {
        public string Name => name;

        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken)
        {
            observer.OnNext(new JobStartedEvent(context));
            return ValueTask.CompletedTask;
        }

        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken)
        {
            observer.OnNext(new JobFinishedEvent(context, jobException));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ForwardingSchedulerListener(string name, IMatcher<TriggerKey> matcher, IObserver<ScheduleChanged> observer) : ISchedulerListener
    {
        public string Name => name;

        public ValueTask JobScheduled(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken) =>
            Forward(trigger.Key);

        public ValueTask JobUnscheduled(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken) =>
            Forward(triggerKey);

        private ValueTask Forward(TriggerKey triggerKey)
        {
            if (matcher.IsMatch(triggerKey))
            {
                observer.OnNext(new ScheduleChanged(triggerKey));
            }

            return ValueTask.CompletedTask;
        }
    }
}
