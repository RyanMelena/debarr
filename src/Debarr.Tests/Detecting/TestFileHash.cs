using System.Security.Cryptography;
using System.Text;
using Debarr.Detecting;

namespace Debarr.Tests.Detecting;

public static class TestFileHash
{
    /// <summary>A valid file hash that differs for each seed, for seeding a video file.</summary>
    public static FileHash For(string seed) => new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed))));
}
