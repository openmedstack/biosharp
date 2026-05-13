using System.Linq;

namespace OpenMedStack.BioSharp.Io.Vcf;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Writes tabix index (.tbi) files for BGZF-compressed, sorted VCF files.
/// The resulting index is compatible with htslib/bcftools tabix.
/// </summary>
public static class TabixIndexWriter
{
    private const int TabixVcfPreset = 2;    // VCF preset
    private const int ColSeq = 1;             // column for sequence name (1-based)
    private const int ColBeg = 2;             // column for start position (1-based)
    private const int ColEnd = 0;             // 0 = use POS+len
    private const int MetaChar = '#';         // meta character
    private const int SkipLines = 0;          // lines to skip

    /// <summary>
    /// Builds a tabix index for a BGZF-compressed VCF file and writes it to
    /// <paramref name="indexPath"/> (conventionally <c>{vcfPath}.tbi</c>).
    /// </summary>
    /// <param name="vcfGzPath">Path to the BGZF-compressed, position-sorted VCF (.vcf.gz).</param>
    /// <param name="indexPath">
    /// Destination path for the index file. Defaults to <paramref name="vcfGzPath"/> + ".tbi".
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task Write(
        string vcfGzPath,
        string? indexPath = null,
        CancellationToken cancellationToken = default)
    {
        indexPath ??= $"{vcfGzPath}.tbi";

        // Pass 1: scan the VCF file and build the index in memory
        var index = await BuildIndex(vcfGzPath, cancellationToken).ConfigureAwait(false);

        // Pass 2: write the index to the .tbi file (itself BGZF-compressed)
        await using var outFile = new FileStream(
            indexPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 65536,
            useAsync: true);

        await using var bgzf = new BgzfStream(outFile, CompressionLevel.Optimal, leaveOpen: false);
        await WriteTbi(bgzf, index, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<TabixIndex> BuildIndex(string vcfGzPath, CancellationToken ct)
    {
        await using var inFile = File.Open(vcfGzPath, new FileStreamOptions
        {
            Access = FileAccess.Read,
            Mode = FileMode.Open,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            Share = FileShare.Read
        });

        await using var bgzf = new BgzfStream(inFile, CompressionMode.Decompress, leaveOpen: false);

        var refIndex = new Dictionary<string, PerRefIndex>(StringComparer.Ordinal);
        var refOrder = new List<string>();
        long noCoord = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var vOffset = bgzf.BlockOffset;
            var line = await ReadLine(bgzf, ct).ConfigureAwait(false);
            if (line == null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith('#'))
            {
                continue;
            }

            // Parse CHROM and POS from the VCF line (tab-separated)
            var tabIdx1 = line.IndexOf('\t');
            if (tabIdx1 < 0)
            {
                continue;
            }

            var chrom = line[..tabIdx1];

            var tabIdx2 = line.IndexOf('\t', tabIdx1 + 1);
            if (tabIdx2 < 0)
            {
                continue;
            }

            if (!int.TryParse(line.AsSpan(tabIdx1 + 1, tabIdx2 - tabIdx1 - 1), out var pos1Based))
            {
                continue;
            }

            // Determine end position (for VCF: same as start for SNPs)
            var endPos = pos1Based; // use POS as default
            var tabIdx4 = line.IndexOf('\t', tabIdx2 + 1);
            var tabIdx5 = tabIdx4 >= 0 ? line.IndexOf('\t', tabIdx4 + 1) : -1;
            if (tabIdx4 >= 0 && tabIdx5 >= 0)
            {
                var refAllele = line[(tabIdx4 + 1)..tabIdx5];
                endPos = pos1Based + refAllele.Length - 1;
            }

            // Get or create per-reference index
            if (!refIndex.TryGetValue(chrom, out var perRef))
            {
                perRef = new PerRefIndex();
                refIndex[chrom] = perRef;
                refOrder.Add(chrom);
            }

            // Convert 1-based VCF position to 0-based for bin calculation
            var beg0 = pos1Based - 1;
            var end0 = endPos;

            var bin = RegionToBin(beg0, end0);
            if (!perRef.Bins.TryGetValue(bin, out var chunks))
            {
                chunks = [];
                perRef.Bins[bin] = chunks;
            }

            // vOffset is the virtual offset of the start of the line
            chunks.Add((vOffset, vOffset)); // single-record chunk; end updated in post-process

            // Linear index: 16 kbp tiles
            var tileIdx = beg0 >> 14;
            while (perRef.LinearIndex.Count <= tileIdx)
                perRef.LinearIndex.Add(ulong.MaxValue);

            if (perRef.LinearIndex[tileIdx] == ulong.MaxValue || vOffset < perRef.LinearIndex[tileIdx])
            {
                perRef.LinearIndex[tileIdx] = vOffset;
            }
        }

        return new TabixIndex(refOrder, refIndex, noCoord);
    }

    private static async Task<string?> ReadLine(Stream stream, CancellationToken cancellationToken)
    {
        using var lineBuffer = new MemoryStream();
        var singleByte = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(singleByte, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (lineBuffer.Length == 0)
                {
                    return null;
                }

                break;
            }

            if (singleByte[0] == '\n')
            {
                break;
            }

            if (singleByte[0] != '\r')
            {
                lineBuffer.WriteByte(singleByte[0]);
            }
        }

        return Encoding.UTF8.GetString(lineBuffer.GetBuffer(), 0, (int)lineBuffer.Length);
    }

    private static async Task WriteTbi(Stream stream, TabixIndex idx, CancellationToken ct)
    {
        // Magic: "TBI\1"
        await stream.WriteAsync(new byte[] { 0x54, 0x42, 0x49, 0x01 }, ct).ConfigureAwait(false);

        // n_ref
        await WriteInt32(stream, idx.RefOrder.Count, ct).ConfigureAwait(false);

        // format (VCF=2), col_seq, col_beg, col_end, meta, skip
        await WriteInt32(stream, TabixVcfPreset, ct).ConfigureAwait(false);
        await WriteInt32(stream, ColSeq, ct).ConfigureAwait(false);
        await WriteInt32(stream, ColBeg, ct).ConfigureAwait(false);
        await WriteInt32(stream, ColEnd, ct).ConfigureAwait(false);
        await WriteInt32(stream, MetaChar, ct).ConfigureAwait(false);
        await WriteInt32(stream, SkipLines, ct).ConfigureAwait(false);

        // l_nm + names (null-terminated, concatenated)
        var namesBuilder = new List<byte>();
        foreach (var name in idx.RefOrder)
        {
            namesBuilder.AddRange(Encoding.UTF8.GetBytes(name));
            namesBuilder.Add(0);
        }

        await WriteInt32(stream, namesBuilder.Count, ct).ConfigureAwait(false);
        await stream.WriteAsync(namesBuilder.ToArray(), ct).ConfigureAwait(false);

        // Per-reference data
        foreach (var refName in idx.RefOrder)
        {
            var perRef = idx.RefData.TryGetValue(refName, out var pr) ? pr : new PerRefIndex();

            // n_bin
            await WriteInt32(stream, perRef.Bins.Count, ct).ConfigureAwait(false);

            foreach (var (bin, chunks) in perRef.Bins)
            {
                await WriteUInt32(stream, (uint)bin, ct).ConfigureAwait(false);
                await WriteInt32(stream, chunks.Count, ct).ConfigureAwait(false);

                foreach (var (begin, end) in chunks)
                {
                    await WriteUInt64(stream, begin, ct).ConfigureAwait(false);
                    await WriteUInt64(stream, end, ct).ConfigureAwait(false);
                }
            }

            // n_intv + linear index
            await WriteInt32(stream, perRef.LinearIndex.Count, ct).ConfigureAwait(false);
            foreach (var effectiveOffset in perRef.LinearIndex.Select(offset => offset == ulong.MaxValue ? 0UL : offset))
            {
                await WriteUInt64(stream, effectiveOffset, ct).ConfigureAwait(false);
            }
        }

        // n_no_coor (optional, 8 bytes)
        await WriteUInt64(stream, (ulong)idx.NoCoord, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Calculates the tabix/BAI bin number for a 0-based [beg, end) half-open interval.
    /// </summary>
    internal static int RegionToBin(int beg, int end)
    {
        end--;
        if (beg >> 14 == end >> 14)
        {
            return ((1 << 15) - 1) / 7 + (beg >> 14);
        }

        if (beg >> 17 == end >> 17)
        {
            return ((1 << 12) - 1) / 7 + (beg >> 17);
        }

        if (beg >> 20 == end >> 20)
        {
            return ((1 << 9) - 1) / 7 + (beg >> 20);
        }

        if (beg >> 23 == end >> 23)
        {
            return ((1 << 6) - 1) / 7 + (beg >> 23);
        }

        if (beg >> 26 == end >> 26)
        {
            return ((1 << 3) - 1) / 7 + (beg >> 26);
        }

        return 0;
    }

    /// <summary>
    /// Returns the set of bin numbers that overlap a 0-based [beg, end) region.
    /// </summary>
    internal static IEnumerable<int> GetOverlappingBins(int beg, int end)
    {
        end--;
        yield return 0;
        for (var k = 1 + (beg >> 26); k <= 1 + (end >> 26); k++)
        {
            yield return k;
        }

        for (var k = 9 + (beg >> 23); k <= 9 + (end >> 23); k++)
        {
            yield return k;
        }

        for (var k = 73 + (beg >> 20); k <= 73 + (end >> 20); k++)
        {
            yield return k;
        }

        for (var k = 585 + (beg >> 17); k <= 585 + (end >> 17); k++)
        {
            yield return k;
        }

        for (var k = 4681 + (beg >> 14); k <= 4681 + (end >> 14); k++)
        {
            yield return k;
        }
    }

    private static async Task WriteInt32(Stream s, int v, CancellationToken ct)
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, v);
        await s.WriteAsync(buf, ct).ConfigureAwait(false);
    }

    private static async Task WriteUInt32(Stream s, uint v, CancellationToken ct)
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, v);
        await s.WriteAsync(buf, ct).ConfigureAwait(false);
    }

    private static async Task WriteUInt64(Stream s, ulong v, CancellationToken ct)
    {
        var buf = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buf, v);
        await s.WriteAsync(buf, ct).ConfigureAwait(false);
    }
}
