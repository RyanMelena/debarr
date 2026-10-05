using Debarr.Components;
using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Debarr.Tests.Components;

public sealed class PageActionTests
{
    private readonly FakeLogger _logger = new();
    private readonly PageAction _action = new();

    [Fact]
    public async Task An_action_is_running_until_it_ends()
    {
        var finish = new TaskCompletionSource<Result>();

        var run = _action.RunAsync(() => finish.Task, _logger, "Nothing was saved.");

        Assert.True(_action.Running);
        finish.SetResult(Result.Ok());
        await run;
        Assert.False(_action.Running);
    }

    [Fact]
    public async Task A_refusal_shows_each_field_error_beneath_its_field_and_the_rest_as_the_error()
    {
        await _action.RunAsync(
            () => Task.FromResult(Result.Fail([new FieldError("Name", "Taken."), new Error("Deleted."), new Error("Renamed.")])),
            _logger,
            "Nothing was saved.");

        Assert.Equal("Taken.", _action.FieldError("Name"));
        Assert.Null(_action.FieldError("Host"));
        Assert.Equal("Deleted. Renamed.", _action.Error);
    }

    [Fact]
    public async Task A_field_error_clears_once_its_field_changes_and_leaves_the_others()
    {
        await _action.RunAsync(
            () => Task.FromResult(Result.Fail([new FieldError("Name", "Taken."), new FieldError("Host", "Enter a host.")])),
            _logger,
            "Nothing was saved.");

        _action.ClearFieldError("Name");

        Assert.Null(_action.FieldError("Name"));
        Assert.Equal("Enter a host.", _action.FieldError("Host"));
    }

    [Fact]
    public async Task The_next_run_clears_the_last_runs_errors_while_it_runs()
    {
        await _action.RunAsync(
            () => Task.FromResult(Result.Fail([new FieldError("Name", "Taken."), new Error("Deleted.")])),
            _logger,
            "Nothing was saved.");
        var finish = new TaskCompletionSource<Result>();

        var run = _action.RunAsync(() => finish.Task, _logger, "Nothing was saved.");

        Assert.Null(_action.FieldError("Name"));
        Assert.Null(_action.Error);
        finish.SetResult(Result.Ok());
        await run;
        Assert.Null(_action.Error);
    }

    [Fact]
    public async Task An_exception_is_logged_and_becomes_the_error()
    {
        await _action.RunAsync(() => Task.FromException(new IOException("Access to the path is denied.")), _logger, "The settings were not saved.");

        Assert.Equal("The settings were not saved. Access to the path is denied. System > Logs has the details.", _action.Error);
        Assert.Equal(LogLevel.Error, _logger.LatestRecord.Level);
        Assert.IsType<IOException>(_logger.LatestRecord.Exception);
        Assert.False(_action.Running);
    }

    [Fact]
    public async Task A_cancellation_still_throws_and_ends_the_run()
    {
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => _action.RunAsync(() => Task.FromCanceled(new CancellationToken(true)), _logger, "Nothing was saved."));

        Assert.False(_action.Running);
    }

    [Fact]
    public async Task The_error_of_several_actions_joins_each_failure_and_is_null_when_none_failed()
    {
        var save = new PageAction();
        var remove = new PageAction();
        Assert.Null(PageAction.ErrorOf(save, remove));

        await save.RunAsync(() => Task.FromResult(Result.Fail("The settings were not saved.")), _logger, "The settings were not saved.");
        await remove.RunAsync(() => Task.FromResult(Result.Fail("The root folder was not removed.")), _logger, "The root folder was not removed.");

        Assert.Equal("The settings were not saved. The root folder was not removed.", PageAction.ErrorOf(save, remove));
    }
}
