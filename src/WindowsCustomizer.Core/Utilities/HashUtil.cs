using System.Security.Cryptography;

namespace WindowsCustomizer.Core.Utilities;

/// <summary>Streaming hash helpers (spec: Security — record hashes of all external artifacts).</summary>
public static class HashUtil
{
    private const int BufferSize = 1024 * 256;

    /// <summary>Computes the SHA-256 of a file as lower-case hex, reporting bytes processed.</summary>
    public static async Task<string> ComputeSha256Async(
        string filePath,
        IProgress<long>? bytesProcessed = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var sha = SHA256.Create();
        var buffer = new byte[BufferSize];
        long total = 0;

        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            sha.TransformBlock(buffer, 0, read, null, 0);
            total += read;
            bytesProcessed?.Report(total);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    /// <summary>Computes the SHA-256 of an in-memory buffer as lower-case hex.</summary>
    public static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
