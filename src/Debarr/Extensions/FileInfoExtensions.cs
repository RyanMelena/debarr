namespace Debarr.Extensions;

public static class FileInfoExtensions
{
    /// <summary>Whether the file opens for reading now.</summary>
    public static bool CanOpen(this FileInfo file)
    {
        try
        {
            using var stream = file.OpenRead();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
