namespace OpenMedStack.BioSharp.Io.FastA;

using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Exception thrown when a reference FASTA fails checksum validation.
/// </summary>
public sealed class ReferenceValidationException : Exception
{
    public ReferenceValidationException(string message) : base(message) { }
}

/// <summary>
/// Checksums computed for a reference FASTA.
/// </summary>
public sealed class ReferenceChecksums
{
    /// <summary>MD5 hex digest of the whole file.</summary>
    public string Md5 { get; init; } = string.Empty;

    /// <summary>SHA-256 hex digest of the whole file.</summary>
    public string Sha256 { get; init; } = string.Empty;
}

/// <summary>
/// Computes and validates MD5 / SHA-256 checksums for reference FASTA files.
/// </summary>
public static class ReferenceValidator
{
    private const int BufferSize = 81_920; // 80 KB

    /// <summary>
    /// Computes MD5 and SHA-256 checksums for a reference FASTA file.
    /// </summary>
    public static async Task<ReferenceChecksums> ComputeChecksumsAsync(
        string fastaPath,
        CancellationToken cancellationToken = default)
    {
        await using var fs = new FileStream(fastaPath, FileMode.Open, FileAccess.Read,
            FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ComputeChecksumsAsync(fs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Computes MD5 and SHA-256 checksums from a stream.
    /// </summary>
    public static async Task<ReferenceChecksums> ComputeChecksumsAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        using var md5 = MD5.Create();
        using var sha256 = SHA256.Create();

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                var chunk = buffer.AsSpan(0, bytesRead);
                md5.TransformBlock(buffer, 0, bytesRead, null, 0);
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
            }

            md5.TransformFinalBlock([], 0, 0);
            sha256.TransformFinalBlock([], 0, 0);

            return new ReferenceChecksums
            {
                Md5 = Convert.ToHexString(md5.Hash!).ToLowerInvariant(),
                Sha256 = Convert.ToHexString(sha256.Hash!).ToLowerInvariant()
            };
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Validates that the file at <paramref name="fastaPath"/> matches <paramref name="expectedMd5"/>.
    /// Throws <see cref="ReferenceValidationException"/> if the checksum does not match.
    /// </summary>
    public static async Task ValidateAsync(
        string fastaPath,
        string expectedMd5,
        CancellationToken cancellationToken = default)
    {
        await using var fs = new FileStream(fastaPath, FileMode.Open, FileAccess.Read,
            FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await ValidateAsync(fs, expectedMd5, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates that the stream content matches <paramref name="expectedMd5"/>.
    /// Throws <see cref="ReferenceValidationException"/> if the checksum does not match.
    /// </summary>
    public static async Task ValidateAsync(
        Stream stream,
        string expectedMd5,
        CancellationToken cancellationToken = default)
    {
        var checksums = await ComputeChecksumsAsync(stream, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(checksums.Md5, expectedMd5.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ReferenceValidationException(
                $"Reference checksum mismatch: expected MD5 '{expectedMd5}' but computed '{checksums.Md5}'.");
        }
    }
}
