using System.ComponentModel;
using CliWrap;
using CliWrap.Buffered;
using Debarr.Hosting;
using FluentResults;
using Microsoft.Extensions.Options;

namespace Debarr.Detecting;

/// <summary>The versions of the configured ffmpeg and ffprobe, each cached once it is read.</summary>
public sealed class FfmpegVersions(IOptions<FfmpegOptions> options)
{
    // Concurrent first reads each run the tool and store the same string.
    private string? _ffmpeg;
    private string? _ffprobe;

    public async Task<Result<string>> GetFfmpegAsync(CancellationToken cancellationToken)
    {
        if (_ffmpeg is { } version)
        {
            return version;
        }

        var result = await ReadAsync("ffmpeg", options.Value.FfmpegPath, cancellationToken);
        _ffmpeg = result.ValueOrDefault;
        return result;
    }

    public async Task<Result<string>> GetFfprobeAsync(CancellationToken cancellationToken)
    {
        if (_ffprobe is { } version)
        {
            return version;
        }

        var result = await ReadAsync("ffprobe", options.Value.FfprobePath, cancellationToken);
        _ffprobe = result.ValueOrDefault;
        return result;
    }

    /// <summary>Runs <c>-version</c> and reads the version from the first line, "&lt;name&gt; version &lt;version&gt; Copyright ...".</summary>
    private static async Task<Result<string>> ReadAsync(string name, string path, CancellationToken cancellationToken)
    {
        BufferedCommandResult result;
        try
        {
            result = await Cli.Wrap(path)
                .WithArguments(["-version"])
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(cancellationToken);
        }
        catch (Win32Exception exception)
        {
            return Result.Fail($"could not run {name}: {exception.Message}");
        }

        if (result.ExitCode != 0)
        {
            return Result.Fail($"{name} -version exited {result.ExitCode}: {result.StandardError.Trim()}");
        }

        var prefix = $"{name} version ";
        var firstLine = result.StandardOutput.Split('\n', 2)[0].Trim();
        return firstLine.StartsWith(prefix, StringComparison.Ordinal)
            ? firstLine[prefix.Length..].Split(' ', 2)[0]
            : firstLine;
    }
}
