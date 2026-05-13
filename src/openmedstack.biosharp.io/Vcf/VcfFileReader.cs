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
using Model;
using OpenMedStack.BioSharp.Model.Vcf;

public class VcfFileReader
{
    public async Task<IHeaderedDisposableAsyncEnumerable<IVariantMetaInformation[], VcfVariant>> Read(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            var fileContent = File.Open(
                path,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                    Share = FileShare.Read
                });
            var stream = new GZipStream(fileContent, CompressionMode.Decompress);
            return await Read(stream, cancellationToken).ConfigureAwait(false);
        }

        var file = File.Open(
            path,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                Share = FileShare.Read
            });
        return await Read(file, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IHeaderedDisposableAsyncEnumerable<IVariantMetaInformation[], VcfVariant>> Read(
        Stream file,
        CancellationToken cancellationToken = default)
    {
        var headers = new List<IVariantMetaInformation>();
        var headerLength = 0;
        string residualText;
        using (var reader = new StreamReader(file, leaveOpen: true))
        {
            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line?.StartsWith("##") == true)
                {
                    headerLength += line.Length + 1;
                    headers.Add(VcfMetaReader.Read(line.AsSpan()));
                }
                else if (line?.StartsWith("#CHROM") == true)
                {
                    headerLength += line.Length + 1;
                    var filePosition = file is GZipStream g ? g.BaseStream.Position : file.Position;
                    var buffer = new char[filePosition - headerLength];
                    var read = 0;
                    while (read < buffer.Length)
                    {
                        var r = await reader.ReadAsync(buffer, read, buffer.Length - read).ConfigureAwait(false);
                        read += r;
                        if (r <= 0)
                        {
                            break;
                        }
                    }

                    residualText = new string(buffer).TrimEnd();
                    break;
                }
                else
                {
                    throw new Exception("Expected content header at this point.");
                }
            }
        }

        return new HeaderedAsyncZipReader<IVariantMetaInformation[], VcfVariant>(
            headers.ToArray(),
            new NoopDisposable(),
            file,
            () => ReadVariants(residualText, file, cancellationToken));
    }

    private static async IAsyncEnumerable<VcfVariant> ReadVariants(
        string buffer,
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (buffer.Length > 0)
        {
            var endsWithline = buffer[^1] != '\n';
            var lines = buffer.Split('\n', StringSplitOptions.TrimEntries);
            for (var i = 0; i < lines.Length - 1; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }

                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                var variant = VcfVariant.Parse(lines[i]);
                if (variant != null)
                {
                    yield return variant;
                }
            }

            if (endsWithline)
            {
                var variant = VcfVariant.Parse(lines[^1]);
                if (variant != null)
                {
                    yield return variant;
                }
            }
        }

        using var reader = new StreamReader(stream);
        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null)
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                var variant = VcfVariant.Parse(line);
                if (variant != null)
                {
                    yield return variant;
                }
            }
        }
    }

    /// <summary>
    /// Performs a region-based query on a BGZF-compressed VCF using a tabix index (.tbi).
    /// Returns only records whose POS overlaps the interval [<paramref name="start"/>, <paramref name="end"/>]
    /// (1-based, inclusive).
    /// </summary>
    /// <param name="vcfGzPath">Path to the BGZF-compressed VCF (.vcf.gz).</param>
    /// <param name="chrom">Chromosome / contig name to query.</param>
    /// <param name="start">1-based inclusive start position.</param>
    /// <param name="end">1-based inclusive end position.</param>
    /// <param name="indexPath">
    /// Path to the .tbi index file. Defaults to <paramref name="vcfGzPath"/> + ".tbi".
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async IAsyncEnumerable<VcfVariant> ReadRegion(
        string vcfGzPath,
        string chrom,
        int start,
        int end,
        string? indexPath = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        indexPath ??= $"{vcfGzPath}.tbi";

        if (!File.Exists(indexPath))
        {
            throw new FileNotFoundException($"Tabix index not found: {indexPath}");
        }

        // Read the tabix index
        var tbi = await ReadTabixIndex(indexPath, cancellationToken).ConfigureAwait(false);

        if (!tbi.TryGetValue(chrom, out var refEntry))
        {
            yield break;
        }

        // Convert to 0-based for bin calculation
        var beg0 = start - 1;
        var end0 = end;

        // Collect virtual offsets from overlapping bins
        var offsets = new SortedSet<ulong>();
        foreach (var bin in TabixIndexWriter.GetOverlappingBins(beg0, end0))
        {
            if (refEntry.Bins.TryGetValue(bin, out var chunks))
            {
                foreach (var (chunkBegin, _) in chunks)
                {
                    offsets.Add(chunkBegin);
                }
            }
        }

        // Also use linear index to find the minimum offset
        ulong minOffset = 0;
        var linearTile = beg0 >> 14;
        if (linearTile < refEntry.LinearIndex.Count)
        {
            minOffset = refEntry.LinearIndex[linearTile];
        }

        // Open VCF and seek to each relevant offset
        await using var vcfFile = File.Open(vcfGzPath, new FileStreamOptions
        {
            Access = FileAccess.Read,
            Mode = FileMode.Open,
            Options = FileOptions.Asynchronous | FileOptions.RandomAccess,
            Share = FileShare.Read
        });

        await using var bgzf = new BgzfStream(vcfFile, CompressionMode.Decompress, leaveOpen: false);

        // Find the minimum relevant offset (from linear index)
        var startOffset = offsets.Count > 0
            ? Math.Max(minOffset, offsets.Min)
            : minOffset;

        // Seek to start offset
        if (startOffset > 0)
        {
            bgzf.Seek((long)startOffset, SeekOrigin.Begin);
        }

        using var reader = new StreamReader(bgzf, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var variant = VcfVariant.Parse(line);
            if (variant == null)
            {
                continue;
            }

            // Stop if we've passed the target chromosome
            if (!string.Equals(variant.Chromosome, chrom, StringComparison.Ordinal))
            {
                break;
            }

            // Stop if we've passed the end of the region
            if (variant.Position > end)
            {
                break;
            }

            // Skip if before start
            if (variant.Position < start)
            {
                continue;
            }

            yield return variant;
        }
    }

    private static async Task<Dictionary<string, TabixRefEntry>> ReadTabixIndex(
        string indexPath,
        CancellationToken ct)
    {
        await using var indexFile = File.Open(indexPath, new FileStreamOptions
        {
            Access = FileAccess.Read,
            Mode = FileMode.Open,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            Share = FileShare.Read
        });

        // TBI files are themselves BGZF-compressed
        await using var bgzf = new BgzfStream(indexFile, CompressionMode.Decompress, leaveOpen: false);

        var magic = new byte[4];
        await bgzf.ReadExactlyAsync(magic, ct).ConfigureAwait(false);

        // Magic: "TBI\1"
        if (magic[0] != 0x54 || magic[1] != 0x42 || magic[2] != 0x49 || magic[3] != 0x01)
        {
            throw new InvalidDataException("Invalid tabix index magic bytes.");
        }

        var intBuf = new byte[4];
        await bgzf.ReadExactlyAsync(intBuf, ct).ConfigureAwait(false);
        var nRef = BinaryPrimitives.ReadInt32LittleEndian(intBuf);

        // format, col_seq, col_beg, col_end, meta, skip
        var headerBuf = new byte[24];
        await bgzf.ReadExactlyAsync(headerBuf, ct).ConfigureAwait(false);

        // l_nm
        await bgzf.ReadExactlyAsync(intBuf, ct).ConfigureAwait(false);
        var lNm = BinaryPrimitives.ReadInt32LittleEndian(intBuf);

        var namesBuf = new byte[lNm];
        await bgzf.ReadExactlyAsync(namesBuf, ct).ConfigureAwait(false);

        // Parse null-terminated names
        var names = new List<string>();
        var start = 0;
        for (var i = 0; i < namesBuf.Length; i++)
        {
            if (namesBuf[i] == 0)
            {
                names.Add(Encoding.UTF8.GetString(namesBuf, start, i - start));
                start = i + 1;
            }
        }

        var result = new Dictionary<string, TabixRefEntry>(StringComparer.Ordinal);

        for (var refIdx = 0; refIdx < nRef; refIdx++)
        {
            var refName = refIdx < names.Count ? names[refIdx] : $"chr{refIdx}";
            var entry = new TabixRefEntry();

            // n_bin
            await bgzf.ReadExactlyAsync(intBuf, ct).ConfigureAwait(false);
            var nBin = BinaryPrimitives.ReadInt32LittleEndian(intBuf);

            for (var b = 0; b < nBin; b++)
            {
                var binBuf = new byte[4];
                await bgzf.ReadExactlyAsync(binBuf, ct).ConfigureAwait(false);
                var bin = BinaryPrimitives.ReadInt32LittleEndian(binBuf);

                await bgzf.ReadExactlyAsync(intBuf, ct).ConfigureAwait(false);
                var nChunk = BinaryPrimitives.ReadInt32LittleEndian(intBuf);

                var chunks = new List<(ulong, ulong)>(nChunk);
                var chunkBuf = new byte[16];
                for (var c = 0; c < nChunk; c++)
                {
                    await bgzf.ReadExactlyAsync(chunkBuf, ct).ConfigureAwait(false);
                    var uOff = BinaryPrimitives.ReadUInt64LittleEndian(chunkBuf.AsSpan(0, 8));
                    var vOff = BinaryPrimitives.ReadUInt64LittleEndian(chunkBuf.AsSpan(8, 8));
                    chunks.Add((uOff, vOff));
                }

                entry.Bins[bin] = chunks;
            }

            // n_intv
            await bgzf.ReadExactlyAsync(intBuf, ct).ConfigureAwait(false);
            var nIntv = BinaryPrimitives.ReadInt32LittleEndian(intBuf);

            var offsetBuf = new byte[8];
            for (var i = 0; i < nIntv; i++)
            {
                await bgzf.ReadExactlyAsync(offsetBuf, ct).ConfigureAwait(false);
                entry.LinearIndex.Add(BinaryPrimitives.ReadUInt64LittleEndian(offsetBuf));
            }

            result[refName] = entry;
        }

        return result;
    }
}
