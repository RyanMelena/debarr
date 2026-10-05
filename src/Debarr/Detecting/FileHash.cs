using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;

namespace Debarr.Detecting;

/// <summary>
/// A video file's identity: SHA-256 of the file size as a little-endian 64-bit integer, then the first and last 1 MiB,
/// or the whole file when it is under 2 MiB, as 64 lowercase hex digits.
/// </summary>
public readonly record struct FileHash
{
    private const int ChunkLength = 1024 * 1024;

    [JsonConstructor]
    public FileHash(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException($"A file hash is 64 lowercase hex digits, not \"{value}\".", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    /// <summary>The video file's stream: the hash's first 16 bytes as a Guid, whose text is the hash's first 32 digits.</summary>
    [JsonIgnore]
    public Guid StreamId => Guid.ParseExact(Value.AsSpan(0, 32), "N");

    public static async Task<FileHash> ComputeAsync(FileInfo file, CancellationToken cancellationToken)
    {
        using var handle = File.OpenHandle(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.Asynchronous);
        var size = RandomAccess.GetLength(handle);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var sizeBytes = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(sizeBytes, size);
        hash.AppendData(sizeBytes);

        var buffer = ArrayPool<byte>.Shared.Rent(ChunkLength);
        try
        {
            if (size < 2 * ChunkLength)
            {
                for (long offset = 0; offset < size; offset += ChunkLength)
                {
                    await AppendRangeAsync(hash, handle, buffer, offset, (int)Math.Min(ChunkLength, size - offset), cancellationToken);
                }
            }
            else
            {
                await AppendRangeAsync(hash, handle, buffer, 0, ChunkLength, cancellationToken);
                await AppendRangeAsync(hash, handle, buffer, size - ChunkLength, ChunkLength, cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return new FileHash(Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    /// <summary>Reads the hash <paramref name="value"/> holds; true when it is 64 lowercase hex digits.</summary>
    public static bool TryParse(string? value, out FileHash fileHash)
    {
        fileHash = IsValid(value) ? new FileHash(value!) : default;
        return IsValid(value);
    }

    public override string ToString() => Value;

    private static bool IsValid(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);

    private static async Task AppendRangeAsync(IncrementalHash hash, SafeFileHandle handle, byte[] buffer, long offset, int length, CancellationToken cancellationToken)
    {
        var filled = 0;
        while (filled < length)
        {
            var read = await RandomAccess.ReadAsync(handle, buffer.AsMemory(filled, length - filled), offset + filled, cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("The file ended before its reported length.");
            }

            filled += read;
        }

        hash.AppendData(buffer, 0, length);
    }
}
