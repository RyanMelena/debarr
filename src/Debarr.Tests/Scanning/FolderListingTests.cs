using Debarr.Scanning;

namespace Debarr.Tests.Scanning;

public sealed class FolderListingTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("debarr-");

    public void Dispose() => _folder.Delete(recursive: true);

    [Fact]
    public void A_folder_lists_its_direct_subfolders_sorted_by_name_ignoring_case()
    {
        _folder.CreateSubdirectory("b");
        _folder.CreateSubdirectory("A").CreateSubdirectory("inner");
        _folder.CreateSubdirectory("C");

        var listing = Read(_folder.FullName);

        Assert.Equal(new LocalPath(_folder.FullName), listing.Folder);
        Assert.Equal([Child("A"), Child("b"), Child("C")], listing.Subfolders);
    }

    [Fact]
    public void A_folder_lists_no_files()
    {
        File.WriteAllText(Path.Combine(_folder.FullName, "film.mkv"), "");
        _folder.CreateSubdirectory("Movies");

        Assert.Equal([Child("Movies")], Read(_folder.FullName).Subfolders);
    }

    [Fact]
    public void A_folder_lists_no_hidden_folders()
    {
        _folder.CreateSubdirectory("Movies");
        if (OperatingSystem.IsWindows())
        {
            var hidden = _folder.CreateSubdirectory("Hidden");
            hidden.Attributes |= FileAttributes.Hidden;
        }
        else
        {
            _folder.CreateSubdirectory(".hidden");
        }

        Assert.Equal([Child("Movies")], Read(_folder.FullName).Subfolders);
    }

    [Fact]
    public void A_folder_with_a_trailing_separator_lists_as_its_canonical_path()
    {
        var listing = Read(_folder.FullName + Path.DirectorySeparatorChar);

        Assert.Equal(new LocalPath(_folder.FullName), listing.Folder);
    }

    [Fact]
    public void A_missing_folder_fails_and_names_the_folder()
    {
        var missing = Path.Combine(_folder.FullName, "missing");

        var listing = FolderListing.Read(new LocalPath(missing));

        Assert.True(listing.IsFailed);
        Assert.Equal($"Debarr cannot find the folder {missing}.", listing.Errors[0].Message);
    }

    [Fact]
    public void Up_from_a_subfolder_is_its_parent()
    {
        var listing = Read(_folder.CreateSubdirectory("Movies").FullName);

        Assert.True(listing.CanGoUp);
        Assert.Equal(new LocalPath(_folder.FullName), listing.Up);
    }

    [Fact]
    public void The_top_is_the_drives_on_windows_and_the_root_elsewhere()
    {
        var top = FolderListing.Read(null).Value;

        Assert.False(top.CanGoUp);
        Assert.Null(top.Up);
        if (OperatingSystem.IsWindows())
        {
            Assert.Null(top.Folder);
            Assert.Contains(new LocalPath(Path.GetPathRoot(_folder.FullName)!), top.Subfolders);
        }
        else
        {
            Assert.Equal(new LocalPath("/"), top.Folder);
            Assert.Contains(new LocalPath("/" + _folder.FullName.Split('/', StringSplitOptions.RemoveEmptyEntries)[0]), top.Subfolders);
        }
    }

    [Fact]
    public void A_root_goes_up_to_the_drives_on_windows_and_is_the_top_elsewhere()
    {
        var root = Read(Path.GetPathRoot(_folder.FullName)!);

        Assert.Equal(new LocalPath(Path.GetPathRoot(_folder.FullName)!), root.Folder);
        Assert.Null(root.Up);
        Assert.Equal(OperatingSystem.IsWindows(), root.CanGoUp);
    }

    private static FolderListing Read(string folder) => FolderListing.Read(new LocalPath(folder)).Value;

    private LocalPath Child(string name) => new(Path.Combine(_folder.FullName, name));
}
