using Debarr.Scanning;

namespace Debarr.Tests.Scanning;

public sealed class LibraryTests
{
    private static readonly DateTimeOffset AddedAt = new(2026, 9, 30, 20, 0, 0, TimeSpan.Zero);

    private static readonly LocalPath Media = new(Path.Combine(Path.GetTempPath(), "debarr-media"));

    private static readonly LocalPath Movies = new(Path.Combine(Media.Value, "movies"));

    private static readonly LocalPath Sibling = new(Media.Value + "2");

    [Fact]
    public void An_added_root_folder_is_enabled()
    {
        var library = Library.Default.Apply(AddRootFolderHandler.Handle(new AddRootFolder(Media, AddedAt), Library.Default));

        Assert.Equal([new RootFolder(Media, true, AddedAt)], library.RootFolders);
    }

    [Fact]
    public void A_root_folder_that_is_already_one_is_inside_one_or_contains_one_is_refused_with_the_other_named()
    {
        Assert.Equal($"{Media.Value} is already a root folder.", RefusalOfAdding(Media, Media));
        Assert.Equal($"{Movies.Value} is inside the root folder {Media.Value}.", RefusalOfAdding(Movies, Media));
        Assert.Equal($"{Media.Value} contains the root folder {Movies.Value}.", RefusalOfAdding(Media, Movies));
    }

    [Fact]
    public void A_sibling_that_shares_a_name_prefix_is_added()
    {
        Assert.True(AddRootFolderHandler.Validate(new AddRootFolder(Sibling, AddedAt), WithRootFolder(Media)).IsSuccess);
        Assert.True(AddRootFolderHandler.Validate(new AddRootFolder(Media, AddedAt), null).IsSuccess);
    }

    [Fact]
    public void A_root_folder_overlapping_a_disabled_one_is_refused()
    {
        var library = WithRootFolder(Media).Apply(new RootFolderDisabled(Media));

        Assert.True(AddRootFolderHandler.Validate(new AddRootFolder(Movies, AddedAt), library).IsFailed);
    }

    [Fact]
    public void Enabling_or_disabling_changes_the_root_folder_only_when_it_differs()
    {
        var library = WithRootFolder(Media);

        Assert.Empty(ValidateAndHandle(new SetRootFolderEnabled(Media, true), library));
        Assert.Equal([new RootFolderDisabled(Media)], ValidateAndHandle(new SetRootFolderEnabled(Media, false), library));

        var disabled = library.Apply(new RootFolderDisabled(Media));
        Assert.False(disabled.FindRootFolder(Media)!.Enabled);
        Assert.Equal([new RootFolderEnabled(Media)], ValidateAndHandle(new SetRootFolderEnabled(Media, true), disabled));
        Assert.True(disabled.Apply(new RootFolderEnabled(Media)).FindRootFolder(Media)!.Enabled);
    }

    [Fact]
    public void Enabling_a_folder_that_is_not_a_root_folder_is_refused()
    {
        Assert.Equal(
            $"{Movies.Value} is no longer a root folder.",
            Assert.Single(SetRootFolderEnabledHandler.Validate(new SetRootFolderEnabled(Movies, true), WithRootFolder(Media)).Errors).Message);
    }

    [Fact]
    public void A_removed_root_folder_leaves_the_library_once()
    {
        var library = WithRootFolder(Media);

        Assert.Equal([new RootFolderRemoved(Media)], RemoveRootFolderHandler.Handle(new RemoveRootFolder(Media), library));
        Assert.Empty(library.Apply(new RootFolderRemoved(Media)).RootFolders);
        Assert.Empty(RemoveRootFolderHandler.Handle(new RemoveRootFolder(Media), Library.Default));
    }

    [Fact]
    public void Saved_settings_normalise_the_video_extensions_and_switch_the_scan_off_with_no_interval()
    {
        var changed = ValidateAndHandle(new ChangeLibrarySettings(" .MKV, mp4  mkv .Avi . ", null, false), Library.Default);

        Assert.Equal("mkv mp4 avi", changed.Settings.VideoExtensions.ToString());
        Assert.Equal((ScanInterval.Off, false), (changed.Settings.ScanInterval, changed.Settings.WatchFolders));
        Assert.Equal(changed.Settings, Library.Default.Apply(changed).Settings);
    }

    [Fact]
    public void Settings_outside_their_bounds_are_refused_beneath_their_fields()
    {
        var errors = ChangeLibrarySettingsHandler.Validate(new ChangeLibrarySettings(" , ", 0, true), Library.Default)
            .Errors.Cast<FieldError>().Select(error => (error.Field, error.Message));

        Assert.Equal(
            [
                ("VideoExtensions", "Enter at least one extension."),
                ("ScanIntervalHours", "Enter 1 or more, or 0 to switch the scheduled scan off."),
            ],
            errors);
    }

    [Fact]
    public void Settings_at_their_bounds_are_saved()
    {
        Assert.True(ChangeLibrarySettingsHandler.Validate(new ChangeLibrarySettings("mkv", 1, true), null).IsSuccess);
        Assert.True(ChangeLibrarySettingsHandler.Validate(new ChangeLibrarySettings("mkv", null, true), null).IsSuccess);
    }

    [Fact]
    public void The_first_root_folder_or_settings_change_starts_the_library_from_its_defaults()
    {
        Assert.Equal([new RootFolder(Media, true, AddedAt)], Library.Create(new RootFolderAdded(Media, AddedAt)).RootFolders);
        var changed = ValidateAndHandle(new ChangeLibrarySettings("mkv", 6, false), null);
        Assert.Equal(Library.Default with { Settings = changed.Settings }, Library.Create(changed));
    }

    [Fact]
    public void A_typed_folder_is_added_at_its_canonical_form()
    {
        var folder = Directory.CreateTempSubdirectory("debarr-root-");
        try
        {
            var command = AddRootFolder.Parse($" {folder.FullName}{Path.DirectorySeparatorChar} ", AddedAt);

            Assert.Equal(new AddRootFolder(new LocalPath(folder.FullName), AddedAt), command.Value);
        }
        finally
        {
            folder.Delete();
        }
    }

    [Fact]
    public void A_relative_path_or_a_missing_folder_is_refused_beneath_the_folder_field()
    {
        var missing = Path.Combine(Path.GetTempPath(), "debarr-missing");

        var relative = Assert.IsType<FieldError>(Assert.Single(AddRootFolder.Parse("movies", AddedAt).Errors));
        var absent = Assert.IsType<FieldError>(Assert.Single(AddRootFolder.Parse(missing, AddedAt).Errors));

        Assert.Equal((nameof(AddRootFolder.Path), "Enter a full path."), (relative.Field, relative.Message));
        Assert.Equal((nameof(AddRootFolder.Path), $"Debarr cannot find the folder {missing}."), (absent.Field, absent.Message));
    }

    private static IReadOnlyList<object> ValidateAndHandle(SetRootFolderEnabled command, Library library) =>
        SetRootFolderEnabledHandler.Handle(command, library, SetRootFolderEnabledHandler.Validate(command, library).Value);

    private static LibrarySettingsChanged ValidateAndHandle(ChangeLibrarySettings command, Library? library) =>
        ChangeLibrarySettingsHandler.Handle(command, library, ChangeLibrarySettingsHandler.Validate(command, library).Value);

    private static Library WithRootFolder(LocalPath path) => Library.Default.Apply(new RootFolderAdded(path, AddedAt));

    private static string RefusalOfAdding(LocalPath path, LocalPath existing) =>
        Assert.Single(AddRootFolderHandler.Validate(new AddRootFolder(path, AddedAt), WithRootFolder(existing)).Errors).Message;
}
