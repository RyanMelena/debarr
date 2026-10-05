using Debarr.Extensions;
using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Debarr.Tests.Extensions;

public sealed class TaskExtensionsTests
{
    private readonly FakeLogger _logger = new();

    [Fact]
    public async Task An_exception_becomes_a_logged_failure_that_says_what_failed_and_points_to_the_log()
    {
        var result = await Task.FromException(new IOException("Access to the path is denied.")).ToResultAsync(_logger, "The settings were not saved.");

        Assert.Equal("The settings were not saved. Access to the path is denied. System > Logs has the details.", result.GetFormError());
        Assert.Equal((LogLevel.Error, "The settings were not saved."), (_logger.LatestRecord.Level, _logger.LatestRecord.Message));
        Assert.IsType<IOException>(_logger.LatestRecord.Exception);
    }

    [Fact]
    public async Task A_refusal_keeps_its_field_errors_apart_from_its_form_error()
    {
        var result = await Task.FromResult(Result.Fail([new FieldError("Name", "Taken."), new Error("Deleted.")])).ToResultAsync(_logger, "Nothing was saved.");

        Assert.Equal(new Dictionary<string, string> { ["Name"] = "Taken." }, result.GetFieldErrors());
        Assert.Equal("Deleted.", result.GetFormError());
        Assert.Empty(_logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task A_cancellation_still_throws() =>
        await Assert.ThrowsAsync<TaskCanceledException>(() => Task.FromCanceled(new CancellationToken(true)).ToResultAsync(_logger, "Nothing was saved."));
}
