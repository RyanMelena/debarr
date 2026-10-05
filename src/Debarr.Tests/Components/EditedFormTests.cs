using Debarr.Components;
using FluentResults;

namespace Debarr.Tests.Components;

public sealed class EditedFormTests
{
    private readonly EditedForm<Form> _form = new(model => model with { });

    [Fact]
    public void The_first_reload_shows_the_saved_values_in_a_copy_the_operator_edits()
    {
        _form.Take(new Form { Name = "Bedroom" });

        _form.Model!.Name = "Theater";

        Assert.Equal("Bedroom", _form.Saved!.Name);
        Assert.True(_form.HasUnsavedChanges);
    }

    [Fact]
    public void A_reload_keeps_unsaved_edits_and_takes_the_new_saved_values()
    {
        _form.Take(new Form { Name = "Bedroom" });
        _form.Model!.Name = "Theater";

        _form.Take(new Form { Name = "Lounge" });

        Assert.Equal("Theater", _form.Model!.Name);
        Assert.Equal("Lounge", _form.Saved!.Name);
        Assert.True(_form.HasUnsavedChanges);
    }

    [Fact]
    public void A_reload_without_unsaved_edits_shows_the_new_saved_values()
    {
        _form.Take(new Form { Name = "Bedroom" });

        _form.Take(new Form { Name = "Lounge" });

        Assert.Equal("Lounge", _form.Model!.Name);
        Assert.False(_form.HasUnsavedChanges);
    }

    [Fact]
    public async Task A_save_that_succeeds_makes_the_values_it_sent_the_saved_ones_and_keeps_edits_made_while_it_ran()
    {
        _form.Take(new Form { Name = "Bedroom" });
        _form.Model!.Name = "Theater";
        var finish = new TaskCompletionSource<Result>();
        Form? sent = null;

        var save = _form.SaveAsync(model => { sent = model; return finish.Task; });
        _form.Model.Name = "Lounge";
        finish.SetResult(Result.Ok());
        await save;

        Assert.Equal("Theater", sent!.Name);
        Assert.Equal("Theater", _form.Saved!.Name);
        Assert.Equal("Lounge", _form.Model.Name);
        Assert.True(_form.HasUnsavedChanges);
    }

    [Fact]
    public async Task A_refused_save_keeps_the_saved_values_and_the_edits()
    {
        _form.Take(new Form { Name = "Bedroom" });
        _form.Model!.Name = "Theater";

        var saved = await _form.SaveAsync(_ => Task.FromResult(Result.Fail(new FieldError(nameof(Form.Name), "Taken."))));

        Assert.Equal("Bedroom", _form.Saved!.Name);
        Assert.Equal("Theater", _form.Model.Name);
        Assert.True(_form.HasUnsavedChanges);
        Assert.Equal("Taken.", Assert.Single(saved.Errors).Message);
    }

    [Fact]
    public async Task A_reload_that_changed_the_saved_values_under_unsaved_edits_refuses_the_save_and_keeps_the_edits()
    {
        _form.Take(new Form { Name = "Bedroom", Note = "" });
        _form.Model!.Name = "Theater";
        _form.Take(new Form { Name = "Bedroom", Note = "Saved in another tab" });
        var sent = false;

        var saved = await _form.SaveAsync(_ => { sent = true; return Task.FromResult(Result.Ok()); });

        Assert.False(sent);
        Assert.Equal("Saved in another tab. Reload to see the change.", Assert.Single(saved.Errors).Message);
        Assert.Equal(new Form { Name = "Theater", Note = "" }, _form.Model);
        Assert.Equal(new Form { Name = "Bedroom", Note = "Saved in another tab" }, _form.Saved);
    }

    [Fact]
    public async Task A_reload_of_the_values_the_form_is_saving_keeps_it_up_to_date()
    {
        _form.Take(new Form { Name = "Bedroom" });
        _form.Model!.Name = "Theater";
        var finish = new TaskCompletionSource<Result>();
        var save = _form.SaveAsync(_ => finish.Task);

        _form.Take(new Form { Name = "Theater" });
        _form.Model.Name = "Lounge";
        finish.SetResult(Result.Ok());
        await save;
        var sent = new List<string>();
        var saved = await _form.SaveAsync(model => { sent.Add(model.Name); return Task.FromResult(Result.Ok()); });

        Assert.Equal(["Lounge"], sent);
        Assert.True(saved.IsSuccess);
    }

    [Fact]
    public void Has_changes_compares_the_way_the_form_says()
    {
        var form = new EditedForm<Form>(model => model with { }, (edited, saved) => edited.Name.Trim() != saved.Name);
        form.Take(new Form { Name = "Bedroom" });

        form.Model!.Name = " Bedroom ";

        Assert.False(form.HasUnsavedChanges);
    }

    private sealed record Form
    {
        public string Name { get; set; } = "";

        public string Note { get; set; } = "";
    }
}
