namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Performs read-backed phasing: assigns variants observed on the same read
/// to the same haplotype within a phase block, emitting phased VCF GT fields.
///
/// <para>
/// Algorithm:
/// <list type="number">
///   <item>For each pair of variants, collect all reads that cover both positions.</item>
///   <item>If the majority of bridging reads carry both alts or both refs, the variants
///         are co-phased (same haplotype).</item>
///   <item>Variants are grouped into contiguous phase blocks; the block boundary is
///         wherever no read bridges two adjacent variants.</item>
///   <item>Each variant receives a <c>PS</c> tag equal to the position of the leftmost
///         variant in its block.</item>
/// </list>
/// </para>
/// </summary>
public sealed class HaplotypePhasingEngine
{
    /// <summary>
    /// Minimum fraction of bridging reads that must agree on allele co-occurrence
    /// to declare a phase relationship (default 0.6 = 60%).
    /// </summary>
    public double MinConcordanceFraction { get; init; } = 0.60;

    /// <summary>
    /// Phases a set of variants using the supplied read spans.
    /// </summary>
    /// <param name="variants">Variants to phase, sorted by position.</param>
    /// <param name="reads">Read spans with per-position allele observations.</param>
    /// <returns>
    /// A <see cref="PhasedVariant"/> for each input variant, in the same order.
    /// </returns>
    public PhasedVariant[] Phase(
        IReadOnlyList<LocalVariantResult> variants,
        IReadOnlyList<ReadSpan> reads)
    {
        var n = variants.Count;
        if (n == 0)
        {
            return [];
        }

        // ── Sort variants by position ────────────────────────────────────────
        var sorted = variants.OrderBy(v => v.Position).ToArray();

        // ── For each variant index, determine which reads cover it ──────────
        var readIndicesByVariant = new List<int>[n];
        for (var i = 0; i < n; i++)
        {
            readIndicesByVariant[i] = [];
        }

        for (var r = 0; r < reads.Count; r++)
        {
            var read = reads[r];
            for (var i = 0; i < n; i++)
            {
                if (read.Covers(sorted[i].Position))
                {
                    readIndicesByVariant[i].Add(r);
                }
            }
        }

        // ── Build adjacency: phased[i][j] = true if i and j are co-phased ──
        // Use a Union-Find to group variants into phase blocks
        var parent = new int[n];
        var rank = new int[n];
        var haplotype = new int[n]; // 0 or 1 within the block
        for (var i = 0; i < n; i++) { parent[i] = i; rank[i] = 0; haplotype[i] = 1; } // default hap=1 (alt)

        for (var i = 0; i < n - 1; i++)
        {
            var j = i + 1;

            // Find reads bridging variants i and j
            var bridging = readIndicesByVariant[i].Intersect(readIndicesByVariant[j]).ToList();
            if (bridging.Count == 0)
            {
                continue;
            }

            // Count read support for co-phasing (both alt) vs cross-phasing (one alt, one ref)
            var coPhasedCount = 0;
            var crossPhasedCount = 0;

            foreach (var ri in bridging)
            {
                var read = reads[ri];
                var isAltI = GetAllele(read, sorted[i].Position);
                var isAltJ = GetAllele(read, sorted[j].Position);

                if (isAltI == isAltJ)
                {
                    coPhasedCount++;
                }
                else
                {
                    crossPhasedCount++;
                }
            }

            var total = coPhasedCount + crossPhasedCount;
            if (total == 0)
            {
                continue;
            }

            var concordance = (double)Math.Max(coPhasedCount, crossPhasedCount) / total;
            if (concordance < MinConcordanceFraction)
            {
                continue;
            }

            // Merge into same phase block
            Union(parent, rank, i, j);

            // Assign relative haplotype: if co-phased, j gets same hap as i;
            // if cross-phased, j gets the opposite hap
            var rootI = Find(parent, i);
            var rootJ = Find(parent, j);
            if (rootI == rootJ)
            {
                // Already merged by Union; adjust haplotype assignment
                if (coPhasedCount >= crossPhasedCount)
                {
                    haplotype[j] = haplotype[i];
                }
                else
                {
                    haplotype[j] = 1 - haplotype[i];
                }
            }
        }

        // ── Assign phase sets and build results ──────────────────────────────
        // A phase set = leftmost position in the block
        var blockToPs = new Dictionary<int, int>(); // root index → phase-set position

        // Only roots that have at least 2 members are "real" phase blocks
        var blockMembers = new Dictionary<int, List<int>>();
        for (var i = 0; i < n; i++)
        {
            var root = Find(parent, i);
            if (!blockMembers.TryGetValue(root, out var list))
            {
                list = [];
                blockMembers[root] = list;
            }

            list.Add(i);
        }

        // Compute PS for each block root (leftmost variant position in block)
        foreach (var (root, members) in blockMembers)
        {
            var psPos = members.Min(idx => sorted[idx].Position);
            blockToPs[root] = psPos;
        }

        var results = new PhasedVariant[n];
        for (var i = 0; i < n; i++)
        {
            var root = Find(parent, i);
            var members = blockMembers[root];
            var isPhased = members.Count >= 2;
            var ps = isPhased ? blockToPs[root] : 0;
            results[i] = new PhasedVariant(sorted[i], isPhased, ps, haplotype[i]);
        }

        return results;
    }

    // ── Union-Find ────────────────────────────────────────────────────────────

    private static int Find(int[] parent, int x)
    {
        while (parent[x] != x)
        {
            parent[x] = parent[parent[x]]; // path compression
            x = parent[x];
        }

        return x;
    }

    private static void Union(int[] parent, int[] rank, int x, int y)
    {
        var rx = Find(parent, x);
        var ry = Find(parent, y);
        if (rx == ry)
        {
            return;
        }

        if (rank[rx] < rank[ry])
        {
            (rx, ry) = (ry, rx);
        }

        parent[ry] = rx;
        if (rank[rx] == rank[ry])
        {
            rank[rx]++;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the allele a read carries at the given position.
    /// True = alt allele; False = ref allele (or position not observed).
    /// </summary>
    private static bool GetAllele(ReadSpan read, int position)
    {
        foreach (var (pos, isAlt) in read.Alleles)
        {
            if (pos == position)
            {
                return isAlt;
            }
        }

        // Position covered by span but no explicit allele entry → assume ref
        return false;
    }
}
