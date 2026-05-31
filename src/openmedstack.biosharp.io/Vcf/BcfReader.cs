using OpenMedStack.BioSharp.Io.Bgzf;

namespace OpenMedStack.BioSharp.Io.Vcf;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Model.Vcf;

/// <summary>
/// Reads BCF version 2.2 files, yielding <see cref="VcfVariant"/> records.
/// BCF is the binary, BGZF-compressed counterpart to text VCF files.
/// </summary>
public class BcfReader : IAsyncDisposable
{
    private static readonly byte[] BcfMagic = [0x42, 0x43, 0x46, 0x02, 0x02]; // "BCF\2\2"

    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private string[]? _contigNames;

    public BcfReader(string path)
    {
        var file = File.Open(path, new FileStreamOptions
        {
            Access = FileAccess.Read,
            Mode = FileMode.Open,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            Share = FileShare.Read
        });
        _stream = new BgzfStream(file, CompressionMode.Decompress, leaveOpen: false);
        _leaveOpen = false;
    }

    public BcfReader(Stream stream, bool leaveOpen = false)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
    }

    /// <summary>
    /// Reads the BCF header and returns the raw VCF header text.
    /// Must be called before iterating records.
    /// </summary>
    public async Task<string> ReadHeader(CancellationToken cancellationToken = default)
    {
        // Verify magic
        var magic = new byte[5];
        await _stream.ReadExactlyAsync(magic, cancellationToken).ConfigureAwait(false);
        if (!magic.AsSpan().SequenceEqual(BcfMagic.AsSpan()))
        {
            throw new InvalidDataException("Invalid BCF magic bytes.");
        }

        // Read header text length
        var lenBuf = new byte[4];
        await _stream.ReadExactlyAsync(lenBuf, cancellationToken).ConfigureAwait(false);
        var headerLen = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);

        if (headerLen is < 0 or > 64 * 1024 * 1024)
        {
            throw new InvalidDataException($"BCF header length out of range: {headerLen}.");
        }

        // Read header text
        var headerBytes = new byte[headerLen];
        await _stream.ReadExactlyAsync(headerBytes, cancellationToken).ConfigureAwait(false);

        // Strip null terminator if present
        var textLen = headerLen;
        while (textLen > 0 && headerBytes[textLen - 1] == 0)
            textLen--;

        var headerText = Encoding.UTF8.GetString(headerBytes, 0, textLen);

        // Parse contig names from header
        _contigNames = ParseContigNames(headerText);

        return headerText;
    }

    /// <summary>
    /// Reads all variant records from the BCF stream.
    /// <see cref="ReadHeader"/> must be called first.
    /// </summary>
    public async IAsyncEnumerable<VcfVariant> ReadVariants(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_contigNames == null)
        {
            throw new InvalidOperationException("Call ReadHeader before reading variants.");
        }

        var lenBuf = new byte[8];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Try to read l_shared + l_indiv (8 bytes)
            var bytesRead = await TryReadExactly(_stream, lenBuf, cancellationToken).ConfigureAwait(false);
            switch (bytesRead)
            {
                case 0:
                    yield break;
                case < 8:
                    throw new InvalidDataException("Truncated BCF record lengths.");
            }

            var lShared = BinaryPrimitives.ReadUInt32LittleEndian(lenBuf.AsSpan(0, 4));
            var lIndiv = BinaryPrimitives.ReadUInt32LittleEndian(lenBuf.AsSpan(4, 4));

            var totalLen = (int)(lShared + lIndiv);
            var recordData = new byte[totalLen];
            await _stream.ReadExactlyAsync(recordData, cancellationToken).ConfigureAwait(false);

            var variant = ParseRecord(recordData.AsSpan(0, (int)lShared), _contigNames);
            if (variant != null)
            {
                yield return variant;
            }
        }
    }

    private static VcfVariant? ParseRecord(ReadOnlySpan<byte> shared, string[] contigNames)
    {
        if (shared.Length < 24)
        {
            return null;
        }

        var chromIdx = BinaryPrimitives.ReadInt32LittleEndian(shared[..4]);
        var pos0Based = BinaryPrimitives.ReadInt32LittleEndian(shared[4..8]);
        // rlen at bytes 8-12 (unused for basic parsing)
        var qualBits = BinaryPrimitives.ReadSingleLittleEndian(shared[12..16]);
        var nInfo = BinaryPrimitives.ReadUInt16LittleEndian(shared[16..18]);
        var nAllele = BinaryPrimitives.ReadUInt16LittleEndian(shared[18..20]);
        // n_sample + n_fmt packed in 4 bytes at 20-24 (unused for basic parsing)

        var chrom = chromIdx >= 0 && chromIdx < contigNames.Length
            ? contigNames[chromIdx]
            : $"chr{chromIdx}";

        var pos1Based = pos0Based + 1;
        var qualValue = float.IsNaN(qualBits) ? 0 : qualBits;
        var qualInt = (int)Math.Round(qualValue);

        // Read ID, REF, ALT typed strings starting at offset 24
        var offset = 24;
        var id = ReadTypedString(shared, ref offset) ?? ".";
        var alleles = new string[nAllele];
        for (var i = 0; i < nAllele && offset < shared.Length; i++)
        {
            alleles[i] = ReadTypedString(shared, ref offset) ?? ".";
        }

        var reference = alleles.Length > 0 ? alleles[0] : ".";
        var alternate = alleles.Length > 1 ? string.Join(",", alleles[1..]) : ".";

        // Read FILTER typed int vector (skip)
        // Read INFO (skip nInfo entries for basic parsing)

        return new VcfVariant
        {
            Chromosome = chrom,
            Position = pos1Based,
            MarkerIdentifiers = id,
            Reference = reference,
            Alternate = alternate,
            ErrorProbabilities = [qualInt],
            FailedFilter = ["PASS"],
            AdditionalInformation = BuildInfoString(shared, ref offset, nInfo)
        };
    }

    private static string? ReadTypedString(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset >= data.Length)
        {
            return null;
        }

        var typeByte = data[offset++];
        var typeId = typeByte & 0x0F;
        var ntype = (typeByte >> 4) & 0x0F;

        int count;
        // Count follows as typed integer
        count = ntype == 15 ? ReadTypedInt(data, ref offset) : ntype;

        if (typeId != 7) // Not a char/string type — skip and return null
        {
            offset += count;
            return null;
        }

        if (offset + count > data.Length)
        {
            return null;
        }

        var s = Encoding.UTF8.GetString(data.Slice(offset, count));
        offset += count;

        // Trim null terminators
        return s.TrimEnd('\0');
    }

    private static int ReadTypedInt(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset >= data.Length)
        {
            return 0;
        }

        var typeByte = data[offset++];
        var typeId = typeByte & 0x0F;
        var ntype = (typeByte >> 4) & 0x0F;

        if (ntype != 1)
        {
            return 0; // Only single int supported here
        }

        return typeId switch
        {
            1 when offset < data.Length => (sbyte)data[offset++],
            2 when offset + 1 < data.Length => BinaryPrimitives.ReadInt16LittleEndian(
                data.Slice(offset, 2)) is var v && (offset += 2) > 0 ? v : 0,
            3 when offset + 3 < data.Length => BinaryPrimitives.ReadInt32LittleEndian(
                data.Slice(offset, 4)) is var v && (offset += 4) > 0 ? v : 0,
            _ => 0
        };
    }

    private static string BuildInfoString(ReadOnlySpan<byte> data, ref int offset, int nInfo)
    {
        // For basic parsing, just return empty string if we can't parse INFO
        _ = offset;
        _ = nInfo;
        _ = data;
        return ".";
    }

    private static string[] ParseContigNames(string headerText)
    {
        var names = new List<string>();
        foreach (var line in headerText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("##contig=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var idStart = line.IndexOf("ID=", StringComparison.OrdinalIgnoreCase);
            if (idStart < 0)
            {
                continue;
            }

            idStart += 3;

            var idEnd = line.IndexOfAny([',', '>'], idStart);
            var name = idEnd < 0 ? line[idStart..] : line[idStart..idEnd];
            names.Add(name.Trim('"'));
        }

        return [.. names];
    }

    private static async Task<int> TryReadExactly(
        Stream stream,
        byte[] buffer,
        CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return totalRead;
            }

            totalRead += read;
        }

        return totalRead;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_leaveOpen)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        GC.SuppressFinalize(this);
    }
}
