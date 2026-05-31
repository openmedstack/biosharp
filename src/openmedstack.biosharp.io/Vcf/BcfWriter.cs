using System.Linq;
using OpenMedStack.BioSharp.Io.Bgzf;

namespace OpenMedStack.BioSharp.Io.Vcf;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Model.Vcf;

/// <summary>
/// Writes BCF version 2.2 (binary VCF) files.
/// Output is BGZF-compressed and compatible with bcftools and htslib.
/// </summary>
public class BcfWriter : IAsyncDisposable
{
    private static readonly byte[] BcfMagic = [0x42, 0x43, 0x46, 0x02, 0x02]; // "BCF\2\2"
    private static readonly float MissingQual;

    static BcfWriter()
    {
        // BCF missing QUAL is NaN with specific bit pattern 0x7F800001
        MissingQual = BitConverter.Int32BitsToSingle(0x7F800001);
    }

    private readonly BgzfStream _bgzf;
    private readonly bool _leaveOpen;
    private readonly List<string> _contigNames = [];
    private bool _headerWritten;

    public BcfWriter(string path)
    {
        var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 65536, useAsync: true);
        _bgzf = new BgzfStream(file, CompressionLevel.Optimal, leaveOpen: false);
        _leaveOpen = false;
    }

    public BcfWriter(Stream stream, bool leaveOpen = false)
    {
        _bgzf = stream as BgzfStream ?? new BgzfStream(stream, CompressionLevel.Optimal, leaveOpen);
        _leaveOpen = leaveOpen;
    }

    /// <summary>
    /// Writes the BCF file header from a VCF-format header string.
    /// The header text should include all ##-prefixed meta-information lines and the #CHROM line.
    /// </summary>
    public async Task WriteHeader(
        IEnumerable<string> contigs,
        string? extraHeaderText = null,
        CancellationToken cancellationToken = default)
    {
        if (_headerWritten)
        {
            throw new InvalidOperationException("Header already written.");
        }

        _contigNames.Clear();
        _contigNames.AddRange(contigs);

        var sb = new StringBuilder();
        sb.AppendLine("##fileformat=VCFv4.2");
        sb.AppendLine("##source=OpenMedStack.BioSharp BcfWriter");

        foreach (var contig in _contigNames)
        {
            sb.AppendLine($"##contig=<ID={contig}>");
        }

        if (!string.IsNullOrEmpty(extraHeaderText))
        {
            sb.Append(extraHeaderText);
        }

        sb.AppendLine("#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO");

        var headerText = sb.ToString();
        var headerBytes = Encoding.UTF8.GetBytes(headerText);

        // Write magic
        await _bgzf.WriteAsync(BcfMagic, cancellationToken).ConfigureAwait(false);

        // Write header length (int32 LE) + header text
        var lenBuf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lenBuf, headerBytes.Length);
        await _bgzf.WriteAsync(lenBuf, cancellationToken).ConfigureAwait(false);
        await _bgzf.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);

        _headerWritten = true;
    }

    /// <summary>
    /// Writes a single <see cref="VcfVariant"/> record to the BCF stream.
    /// </summary>
    public async Task WriteVariant(VcfVariant variant, CancellationToken cancellationToken = default)
    {
        if (!_headerWritten)
        {
            throw new InvalidOperationException("Call WriteHeaderAsync before writing variants.");
        }

        var shared = BuildSharedData(variant);
        var lenBuf = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(lenBuf.AsSpan(0, 4), (uint)shared.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(lenBuf.AsSpan(4, 4), 0); // l_indiv = 0

        await _bgzf.WriteAsync(lenBuf, cancellationToken).ConfigureAwait(false);
        await _bgzf.WriteAsync(shared, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes multiple variants.
    /// </summary>
    public async Task WriteVariants(
        IEnumerable<VcfVariant> variants,
        CancellationToken cancellationToken = default)
    {
        foreach (var v in variants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteVariant(v, cancellationToken).ConfigureAwait(false);
        }
    }

    private byte[] BuildSharedData(VcfVariant variant)
    {
        var buf = new List<byte>(256);

        // CHROM: int32 LE
        var chromIdx = _contigNames.IndexOf(variant.Chromosome);
        if (chromIdx < 0)
        {
            chromIdx = 0; // unknown contig → index 0
        }

        AppendInt32(buf, chromIdx);

        // POS: int32 LE (0-based)
        AppendInt32(buf, variant.Position - 1);

        // rlen: int32 LE (length of REF allele; for unknown, use 0)
        AppendInt32(buf, variant.Reference?.Length ?? 0);

        // qual: float32 LE
        var qual = variant.ErrorProbabilities?.Length > 0
            ? variant.ErrorProbabilities[0]
            : MissingQual;
        var qualBytes = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(qualBytes, qual);
        buf.AddRange(qualBytes);

        // n_info: uint16 LE (0 INFO fields written)
        AppendUInt16(buf, 0);

        // n_allele: uint16 LE
        var alleles = BuildAlleleList(variant);
        AppendUInt16(buf, (ushort)alleles.Count);

        // n_sample (low 24 bits) + n_fmt (high 8 bits): uint32 LE
        AppendUInt32(buf, 0);

        // ID: typed string
        AppendTypedString(buf, variant.MarkerIdentifiers ?? ".");

        // REF + ALT alleles as typed strings
        foreach (var allele in alleles)
        {
            AppendTypedString(buf, allele);
        }

        // FILTER: typed int8 vector [0] = PASS (index 0)
        buf.Add(0x11); // type=INT8, count=1
        buf.Add(0);    // PASS index

        // INFO: empty (n_info = 0)

        return [.. buf];
    }

    private static List<string> BuildAlleleList(VcfVariant variant)
    {
        var alleles = new List<string> { string.IsNullOrEmpty(variant.Reference) ? "." : variant.Reference };

        if (!string.IsNullOrEmpty(variant.Alternate) && variant.Alternate != ".")
        {
            alleles.AddRange(variant.Alternate.Split(',').Select(alt => alt.Trim()));
        }
        else
        {
            alleles.Add(".");
        }

        return alleles;
    }

    private static void AppendTypedString(List<byte> buf, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var len = bytes.Length;

        if (len < 15)
        {
            buf.Add((byte)((len << 4) | 0x07)); // type=CHAR, count=len
        }
        else
        {
            buf.Add(0xF7); // type=CHAR, count=15 (overflow)
            // Append count as typed int
            AppendTypedInt(buf, len);
        }

        buf.AddRange(bytes);
    }

    private static void AppendTypedInt(List<byte> buf, int value)
    {
        if (value <= 127)
        {
            buf.Add(0x11); // type=INT8, count=1
            buf.Add((byte)(sbyte)value);
        }
        else if (value <= 32767)
        {
            buf.Add(0x12); // type=INT16, count=1
            var b = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(b, (short)value);
            buf.AddRange(b);
        }
        else
        {
            buf.Add(0x13); // type=INT32, count=1
            AppendInt32(buf, value);
        }
    }

    private static void AppendInt32(List<byte> buf, int value)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b, value);
        buf.AddRange(b);
    }

    private static void AppendUInt16(List<byte> buf, ushort value)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, value);
        buf.AddRange(b);
    }

    private static void AppendUInt32(List<byte> buf, uint value)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, value);
        buf.AddRange(b);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _bgzf.FlushAsync().ConfigureAwait(false);
        if (!_leaveOpen)
        {
            await _bgzf.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }
}
