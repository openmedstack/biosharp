namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Model;

/// <summary>
/// Writes gVCF (genomic VCF) output — a VCF 4.2 file that emits a standard variant
/// record for each called site and a reference block record (<c>&lt;NON_REF&gt;</c>)
/// for every uncalled reference position, grouped into blocks of the same
/// genotype-quality tier.
///
/// <para>Genotype-quality (GQ) tiers used for block grouping:</para>
/// <list type="bullet">
///   <item>Tier 0  — GQ  0–9</item>
///   <item>Tier 1  — GQ 10–19</item>
///   <item>Tier 2  — GQ 20–29</item>
///   <item>Tier 3  — GQ 30+</item>
/// </list>
///
/// <para>Each reference block includes the mandatory gVCF INFO fields
/// <c>END=N</c>, <c>MIN_DP=X</c> and the FORMAT field <c>GQ</c>.</para>
/// </summary>
public static class GvcfWriter
{
    private static readonly int[] GqTierBoundaries = [0, 10, 20, 30, int.MaxValue];

    /// <summary>
    /// Returns the GQ tier index (0–3) for a given depth-derived GQ value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetGqTier(int gq)
    {
        for (var t = 0; t < GqTierBoundaries.Length - 1; t++)
        {
            if (gq < GqTierBoundaries[t + 1])
            {
                return t;
            }
        }

        return GqTierBoundaries.Length - 2;
    }

    /// <summary>
    /// Estimate a simple Phred-scaled GQ from depth. Real callers use
    /// likelihood models; here we use a simplified heuristic:
    /// GQ ≈ min(99, depth × 3).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DepthToGq(int depth) => Math.Min(99, depth * 3);

    /// <summary>
    /// Writes a gVCF file to <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">Destination stream.</param>
    /// <param name="variants">Called variants (must be sorted by position).</param>
    /// <param name="referenceSequence">
    /// The full reference sequence for the chromosome, 0-based (position 1 in VCF = index 0).
    /// </param>
    /// <param name="chromosome">Chromosome name emitted in every record.</param>
    /// <param name="depths">
    /// Per-position read depth, 0-based, parallel to <paramref name="referenceSequence"/>.
    /// Must have the same length as <paramref name="referenceSequence"/>.
    /// </param>
    /// <param name="chromLength">Optional length for the contig header.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task WriteAsync(
        Stream stream,
        IEnumerable<LocalVariantResult> variants,
        ReadOnlyMemory<char> referenceSequence,
        string chromosome,
        int[] depths,
        long? chromLength = null,
        CancellationToken cancellationToken = default)
    {
        var refLen = referenceSequence.Length;
        if (depths.Length < refLen)
        {
            throw new ArgumentException("depths array must be at least as long as referenceSequence.", nameof(depths));
        }

        var variantsByPos = new Dictionary<int, LocalVariantResult>();
        foreach (var v in variants)
        {
            variantsByPos[v.Position] = v; // 1-based
        }

        // Copy reference to array so we can safely access it across awaits
        var refArray = referenceSequence.ToArray();

        // ── Header ───────────────────────────────────────────────────────────
        await WriteHeaderAsync(stream, chromosome, chromLength, cancellationToken).ConfigureAwait(false);

        // ── Records ─────────────────────────────────────────────────────────
        // Walk 1-based positions 1..refLen
        var pos = 1;

        while (pos <= refLen)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (variantsByPos.TryGetValue(pos, out var variant))
            {
                // Emit standard variant record
                await WriteVariantRecordAsync(stream, variant, chromosome, cancellationToken)
                    .ConfigureAwait(false);
                pos++;
            }
            else
            {
                // Start a reference block — extend while same GQ tier and no variant
                var blockStart = pos;
                var minDepth = depths[pos - 1];
                var startTier = GetGqTier(DepthToGq(depths[pos - 1]));

                pos++;
                while (pos <= refLen
                       && !variantsByPos.ContainsKey(pos)
                       && GetGqTier(DepthToGq(depths[pos - 1])) == startTier)
                {
                    if (depths[pos - 1] < minDepth)
                    {
                        minDepth = depths[pos - 1];
                    }

                    pos++;
                }

                var blockEnd = pos - 1;
                var refBase = refArray[blockStart - 1]; // 0-based index
                var gqValue = GqTierBoundaries[startTier]; // lower bound of this tier

                await WriteRefBlockAsync(
                    stream, chromosome, blockStart, blockEnd,
                    refBase, minDepth, gqValue, cancellationToken).ConfigureAwait(false);
            }
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static async Task WriteHeaderAsync(
        Stream stream,
        string chromosome,
        long? chromLength,
        CancellationToken ct)
    {
        var sb = new StringBuilder(512);
        sb.AppendLine("##fileformat=VCFv4.2");
        sb.AppendLine("##source=OpenMedStack.BioSharp GvcfWriter");
        sb.AppendLine("##ALT=<ID=NON_REF,Description=\"Represents any possible alternative allele at this location\">");
        sb.AppendLine("##INFO=<ID=END,Number=1,Type=Integer,Description=\"End position of reference block\">");
        sb.AppendLine("##INFO=<ID=MIN_DP,Number=1,Type=Integer,Description=\"Minimum read depth in block\">");
        sb.AppendLine("##INFO=<ID=DP,Number=1,Type=Integer,Description=\"Total read depth at the position\">");
        if (chromLength.HasValue)
        {
            sb.AppendLine($"##contig=<ID={chromosome},length={chromLength.Value}>");
        }

        sb.AppendLine("##FORMAT=<ID=GT,Number=1,Type=String,Description=\"Genotype\">");
        sb.AppendLine("##FORMAT=<ID=GQ,Number=1,Type=Integer,Description=\"Genotype Quality\">");
        sb.AppendLine("##FORMAT=<ID=DP,Number=1,Type=Integer,Description=\"Read depth\">");
        sb.AppendLine("#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO\tFORMAT\tSAMPLE");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
    }

    private static async Task WriteVariantRecordAsync(
        Stream stream,
        LocalVariantResult variant,
        string chromosome,
        CancellationToken ct)
    {
        var filter = variant.IsPass ? "PASS" : "LOW_QUAL";
        var genoStr = variant.Genotype != null
            ? variant.Genotype.ToVcfGenotype() + ":" + variant.Genotype.GQ + ":" +
              (variant.Genotype.RefCoverage + variant.Genotype.AltCoverage)
            : "0/1:50:" + variant.Depth;

        var line = new StringBuilder(128);
        line.Append(chromosome).Append('\t')
            .Append(variant.Position).Append('\t')
            .Append('.').Append('\t')
            .Append(variant.Reference).Append('\t')
            .Append(string.Join(",", variant.AllAlternateAlleles)).Append('\t')
            .Append(variant.QuantitativeQuality).Append('\t')
            .Append(filter).Append('\t')
            .Append("DP=").Append(variant.Depth).Append('\t')
            .Append("GT:GQ:DP").Append('\t')
            .Append(genoStr)
            .Append('\n');

        var bytes = Encoding.UTF8.GetBytes(line.ToString());
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
    }

    private static async Task WriteRefBlockAsync(
        Stream stream,
        string chromosome,
        int startPos,
        int endPos,
        char refBase,
        int minDepth,
        int gqValue,
        CancellationToken ct)
    {
        // FORMAT: GT:GQ:DP — 0/0 for reference blocks
        var line = new StringBuilder(128);
        line.Append(chromosome).Append('\t')
            .Append(startPos).Append('\t')
            .Append('.').Append('\t')
            .Append(refBase).Append('\t')
            .Append("<NON_REF>").Append('\t')
            .Append('.').Append('\t')
            .Append('.').Append('\t')
            .Append("END=").Append(endPos)
            .Append(";MIN_DP=").Append(minDepth).Append('\t')
            .Append("GT:GQ:DP").Append('\t')
            .Append("0/0:").Append(gqValue).Append(':').Append(minDepth)
            .Append('\n');

        var bytes = Encoding.UTF8.GetBytes(line.ToString());
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
    }
}
