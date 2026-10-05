using System.Globalization;

namespace Debarr.Extensions;

public static class Int64Extensions
{
    /// <summary>A file size in bytes, in 1024-based units, such as "512 B", "1.5 KB" or "2.3 MB".</summary>
    public static string ToFileSizeText(this long bytes)
    {
        string[] units = ["KB", "MB", "GB", "TB"];

        if (bytes < 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
        }

        var size = bytes / 1024.0;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{size:0.#} {units[unit]}");
    }
}
