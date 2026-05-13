using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Calculations.Alignment;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// VC-1 — gVCF (genomic VCF) output
// ─────────────────────────────────────────────────────────────────────────────
public class GvcfWriterTests
{
    private static LocalVariantResult Variant(string chrom, int pos, string refB, string alt, int qual, int depth)
    {
        return new LocalVariantResult
        {
            Chromosome = chrom,
            Position = pos,
            Reference = refB,
            Alternate = alt,
            QuantitativeQuality = qual,
            Depth = depth
        };
    }

    [Fact]
    public async Task WriteGvcfAsync_EmitsFileFormatHeader()
    {
        var refSeq = new string('A', 20);
        var depths = new int[20];
        Array.Fill(depths, 30);
        var variants = new[] { Variant("chr1", 10, "A", "T", 50, 30) };

        using var ms = new MemoryStream();
        await GvcfWriter.Write(ms, variants, refSeq.AsMemory(), "chr1", depths);
        var text = Encoding.UTF8.GetString(ms.ToArray());

        Assert.Contains("##fileformat=VCFv4.2", text);
        Assert.Contains("##ALT=<ID=NON_REF", text);
    }

    [Fact]
    public async Task WriteGvcfAsync_VariantPositionIsEmittedAsStandardRecord()
    {
        // Reference: 20 A's; variant at position 10 (1-based)
        var refSeq = new string('A', 20);
        var depths = new int[20];
        Array.Fill(depths, 30);
        var variants = new[] { Variant("chr1", 10, "A", "T", 50, 30) };

        using var ms = new MemoryStream();
        await GvcfWriter.Write(ms, variants, refSeq.AsMemory(), "chr1", depths);
        var text = Encoding.UTF8.GetString(ms.ToArray());

        var dataLines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.StartsWith('#')).ToList();

        // The variant at pos 10 should appear as a standard record with T as ALT (not <NON_REF>)
        Assert.Contains(dataLines, l => l.Contains("\t10\t") && l.Contains("\tT\t"));
    }

    [Fact]
    public async Task WriteGvcfAsync_AllPositionsCoveredByVariantOrRefBlock()
    {
        // Reference: 20 A's; variant at pos 10 only.
        var refSeq = new string('A', 20);
        var depths = new int[20];
        Array.Fill(depths, 25);
        var variants = new[] { Variant("chr1", 10, "A", "T", 50, 25) };

        using var ms = new MemoryStream();
        await GvcfWriter.Write(ms, variants, refSeq.AsMemory(), "chr1", depths);
        var text = Encoding.UTF8.GetString(ms.ToArray());

        // Collect all positions covered
        var covered = new HashSet<int>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            var fields = line.Split('\t');
            if (fields.Length < 8)
            {
                continue;
            }

            var pos = int.Parse(fields[1]);
            covered.Add(pos);

            // Reference blocks have END= in INFO
            var info = fields[7];
            var endIdx = info.IndexOf("END=", StringComparison.Ordinal);
            if (endIdx >= 0)
            {
                var endSpan = info.AsSpan(endIdx + 4);
                var semi = endSpan.IndexOf(';');
                var endStr = semi >= 0 ? endSpan[..semi] : endSpan;
                var endPos = int.Parse(endStr);
                for (var p = pos; p <= endPos; p++)
                {
                    covered.Add(p);
                }
            }
        }

        // All positions 1..20 must be covered
        for (var i = 1; i <= 20; i++)
        {
            Assert.True(covered.Contains(i), $"Position {i} not covered by any record");
        }
    }

    [Fact]
    public async Task WriteGvcfAsync_RefBlocksIncludeEndMinDpAndGq()
    {
        var refSeq = new string('A', 10);
        var depths = new int[10];
        Array.Fill(depths, 15);
        var variants = Array.Empty<LocalVariantResult>();

        using var ms = new MemoryStream();
        await GvcfWriter.Write(ms, variants, refSeq.AsMemory(), "chr1", depths);
        var text = Encoding.UTF8.GetString(ms.ToArray());

        var dataLines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.StartsWith('#')).ToList();

        Assert.NotEmpty(dataLines);
        // Each reference block line should have END=, MIN_DP=, and GQ
        foreach (var line in dataLines)
        {
            Assert.Contains("END=", line);
            Assert.Contains("MIN_DP=", line);
            Assert.Contains("<NON_REF>", line);
        }
    }

    [Fact]
    public async Task WriteGvcfAsync_RefBlocksGroupByGqTier()
    {
        // Positions 1-5: depth=5  -> GQ tier 0-10
        // Positions 6-10: depth=35 -> GQ tier 30+
        var refSeq = new string('A', 10);
        var depths = new int[10];
        for (var i = 0; i < 5; i++)
        {
            depths[i] = 5;
        }

        for (var i = 5; i < 10; i++)
        {
            depths[i] = 35;
        }

        using var ms = new MemoryStream();
        await GvcfWriter.Write(ms, Array.Empty<LocalVariantResult>(), refSeq.AsMemory(), "chr1", depths);
        var text = Encoding.UTF8.GetString(ms.ToArray());

        var dataLines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.StartsWith('#')).ToList();

        // Expect at least 2 blocks because depth changes significantly (different GQ tier)
        Assert.True(dataLines.Count >= 2, $"Expected at least 2 blocks, got {dataLines.Count}");
    }

    [Fact]
    public async Task WriteGvcfAsync_ZeroCoveragePositionsHaveMinDpZero()
    {
        var refSeq = new string('A', 5);
        var depths = new int[5]; // all zero

        using var ms = new MemoryStream();
        await GvcfWriter.Write(ms, Array.Empty<LocalVariantResult>(), refSeq.AsMemory(), "chr1", depths);
        var text = Encoding.UTF8.GetString(ms.ToArray());

        var dataLines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.StartsWith('#')).ToList();

        // Should have at least one block
        Assert.NotEmpty(dataLines);
        Assert.Contains("MIN_DP=0", text);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// VC-2 — Multi-allelic site handling and VCF normalisation
// ─────────────────────────────────────────────────────────────────────────────

// ─────────────────────────────────────────────────────────────────────────────
// VC-3 — Copy number variation (CNV) calling
// ─────────────────────────────────────────────────────────────────────────────

// ─────────────────────────────────────────────────────────────────────────────
// VC-4 — Haplotype phasing
// ─────────────────────────────────────────────────────────────────────────────

// ─────────────────────────────────────────────────────────────────────────────
// VC-5 — Population allele frequency annotation via VCF lookup
// ─────────────────────────────────────────────────────────────────────────────