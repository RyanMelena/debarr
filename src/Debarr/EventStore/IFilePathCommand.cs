using Debarr.Scanning;

namespace Debarr.EventStore;

/// <summary>A command a scan or a detection sends for one file path, once per file across the library, so its line names the path and a success logs at Debug.</summary>
public interface IFilePathCommand
{
    LocalPath Path { get; }
}
