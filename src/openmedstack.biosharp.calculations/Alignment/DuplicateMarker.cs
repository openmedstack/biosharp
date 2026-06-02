using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Io.Sam;

/// <summary>
/// Marks PCR and optical duplicate reads in a collection of <see cref="AlignmentSection"/> records.
///
/// Duplicates are identified by the signature (reference sequence index, 5′ mapping position,
/// orientation, CIGAR string). Within each duplicate group the highest-MAPQ read is retained
/// as the primary; all others are flagged with <see cref="AlignmentSection.AlignmentFlag.PcrOrOpticalDuplicate"/>.
///
/// Optical duplicates (reads from the same tile with x/y coordinates within a configurable
/// pixel distance) are detected from the Illumina read-name format:
///   @Instrument:RunID:FlowcellID:Lane:Tile:X:Y
/// </summary>
public static class DuplicateMarker
{
    // Signature used to group potential duplicates.
    private readonly record struct DuplicateKey(
        string ReferenceName,
        int Position,
        bool ReverseStrand,
        string CigarSignature);

    // Parsed Illumina tile coordinates used for optical duplicate detection.
    private readonly record struct TileCoords(string FlowcellId, int Lane, int Tile, int X, int Y);

    /// <summary>
    /// Marks duplicate reads in <paramref name="alignments"/> and returns the annotated list
    /// together with processing metrics.
    /// The sequence is materialised internally; the caller does not need to buffer it.
    /// </summary>
    /// <param name="alignments">Input alignments (unmapped reads are ignored).</param>
    /// <param name="opticalDuplicatePixelDistance">
    /// Maximum Euclidean pixel distance within the same tile to call an optical duplicate.
    /// Defaults to 100.
    /// </param>
    public static (IReadOnlyList<AlignmentSection> Marked, DuplicateMetrics Metrics) MarkDuplicates(
        IEnumerable<AlignmentSection> alignments,
        int opticalDuplicatePixelDistance = 100)
    {
        var arr = alignments as AlignmentSection[] ?? alignments.ToArray();
        return MarkDuplicatesCore(arr, opticalDuplicatePixelDistance);
    }

    /// <summary>
    /// Async variant of <see cref="MarkDuplicates"/> that accepts a streamed sequence.
    /// Records are collected in-memory before duplicate detection begins.
    /// </summary>
    /// <param name="alignments">Async stream of alignments (unmapped reads are ignored).</param>
    /// <param name="opticalDuplicatePixelDistance">
    /// Maximum Euclidean pixel distance within the same tile to call an optical duplicate.
    /// Defaults to 100.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<(IReadOnlyList<AlignmentSection> Marked, DuplicateMetrics Metrics)> MarkDuplicatesAsync(
        IAsyncEnumerable<AlignmentSection> alignments,
        int opticalDuplicatePixelDistance = 100,
        CancellationToken cancellationToken = default)
    {
        var list = new List<AlignmentSection>();
        await foreach (var section in alignments.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            list.Add(section);
        }

        return MarkDuplicatesCore(list.ToArray(), opticalDuplicatePixelDistance);
    }

    private static (IReadOnlyList<AlignmentSection> Marked, DuplicateMetrics Metrics) MarkDuplicatesCore(
        AlignmentSection[] alignments,
        int opticalDuplicatePixelDistance)
    {
        var metrics = new DuplicateMetrics { TotalReads = alignments.Length };
        var result = new AlignmentSection[alignments.Length];
        for (var i = 0; i < alignments.Length; i++)
        {
            result[i] = alignments[i];
        }

        // Separate paired-end and single-end reads for grouping purposes.
        // For paired-end, we use the primary (first-in-template) position as the duplicate key.
        // For single-end (or unmapped-mate), we use the read's own position.

        // Group indices by their duplicate key.
        var groups = new Dictionary<DuplicateKey, List<int>>(capacity: alignments.Length);

        for (var i = 0; i < alignments.Length; i++)
        {
            var a = alignments[i];

            // Skip unmapped reads
            if ((a.Flag & AlignmentSection.AlignmentFlag.SegmentUnmapped) != 0)
            {
                continue;
            }

            var key = GetDuplicateKey(a);
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<int>(4);
                groups[key] = list;
            }
            list.Add(i);
        }

        // For paired-end reads, further group by query name so the two mates share a group.
        // Then use the combined quality of both mates to select the primary pair.
        var pairedGroups = new Dictionary<DuplicateKey, Dictionary<string, List<int>>>();

        foreach (var (key, indices) in groups)
        {
            if (indices.Count <= 1)
            {
                continue; // no duplicate group
            }

            // Separate into paired and unpaired within this positional group
            var byName = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            foreach (var idx in indices)
            {
                var a = alignments[idx];
                var isPaired = (a.Flag & AlignmentSection.AlignmentFlag.MultipleSegments) != 0;
                var groupName = isPaired ? a.QName : $"\0{idx}"; // unique for singletons
                if (!byName.TryGetValue(groupName, out var lst))
                {
                    lst = new List<int>(2);
                    byName[groupName] = lst;
                }
                lst.Add(idx);
            }

            if (byName.Count <= 1)
            {
                continue; // all from same template — already a group
            }

            pairedGroups[key] = byName;
        }

        // Mark duplicates within each positional group
        foreach (var (_, byName) in pairedGroups)
        {
            // Score each template (or singleton) by total MAPQ across mates
            var scored = byName
                .Select(kvp => (name: kvp.Key, indices: kvp.Value,
                    score: kvp.Value.Sum(i => alignments[i].MappingQuality)))
                .OrderByDescending(x => x.score)
                .ToList();

            // Primary is the highest-scored template; all others are duplicates
            for (var rank = 1; rank < scored.Count; rank++)
            {
                foreach (var idx in scored[rank].indices)
                {
                    var a = alignments[idx];
                    result[idx] = new AlignmentSection(
                        a.QName, a.Flag | AlignmentSection.AlignmentFlag.PcrOrOpticalDuplicate,
                        a.ReadName, a.Position, a.MappingQuality, a.Cigar,
                        a.ReferenceIdOfNextSegment, a.NextPosition, a.TemplateLength,
                        a.Sequence, a.Quality, a.Tags, a.Index);
                    metrics.DuplicateReads++;
                }
            }

            // Optical-duplicate detection within this group
            var allInGroup = scored.SelectMany(s => s.indices).ToList();
            MarkOpticalDuplicates(result, allInGroup, opticalDuplicatePixelDistance, metrics);
        }

        return (result, metrics);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static DuplicateKey GetDuplicateKey(AlignmentSection a)
    {
        var reverseStrand = (a.Flag & AlignmentSection.AlignmentFlag.SeqBeingReverseComplemented) != 0;
        return new DuplicateKey(a.ReadName, a.Position, reverseStrand, BuildCigarString(a.Cigar));
    }

    private static string BuildCigarString(ReadOnlySpan<(uint count, CigarOp op)> cigar)
    {
        if (cigar.IsEmpty)
        {
            return "*";
        }

        var sb = new System.Text.StringBuilder(cigar.Length * 4);
        foreach (var (count, op) in cigar)
        {
            sb.Append(count).Append((char)('M' + (int)op));
        }

        return sb.ToString();
    }

    private static void MarkOpticalDuplicates(
        AlignmentSection[] result,
        List<int> groupIndices,
        int maxPixelDistance,
        DuplicateMetrics metrics)
    {
        // Parse tile coordinates for all reads in the group
        var coords = new List<(int idx, TileCoords? tile)>(groupIndices.Count);
        coords.AddRange(groupIndices.Select(idx => (idx, ParseTileCoords(result[idx].QName))));

        // Compare every pair within same tile
        for (var i = 0; i < coords.Count - 1; i++)
        {
            var (idxA, tileA) = coords[i];
            if (tileA is null)
            {
                continue;
            }

            for (var j = i + 1; j < coords.Count; j++)
            {
                var (idxB, tileB) = coords[j];
                if (tileB is null)
                {
                    continue;
                }

                if (tileA.Value.FlowcellId != tileB.Value.FlowcellId)
                {
                    continue;
                }

                if (tileA.Value.Lane != tileB.Value.Lane)
                {
                    continue;
                }

                if (tileA.Value.Tile != tileB.Value.Tile)
                {
                    continue;
                }

                var dx = tileA.Value.X - tileB.Value.X;
                var dy = tileA.Value.Y - tileB.Value.Y;
                var distSq = (long)dx * dx + (long)dy * dy;
                if (distSq > (long)maxPixelDistance * maxPixelDistance)
                {
                    continue;
                }

                // Both are already in the duplicate group; track optical count for the
                // non-primary one (which is already marked as a duplicate).
                var nonPrimary = result[idxA].Flag.HasFlag(AlignmentSection.AlignmentFlag.PcrOrOpticalDuplicate)
                    ? idxA
                    : idxB;
                if (result[nonPrimary].Flag.HasFlag(AlignmentSection.AlignmentFlag.PcrOrOpticalDuplicate))
                {
                    metrics.OpticalDuplicateReads++;
                }
            }
        }
    }

    /// <summary>
    /// Attempts to parse the Illumina read-name format:
    ///   @Instrument:RunID:FlowcellID:Lane:Tile:X:Y
    /// Returns null if the name does not match.
    /// </summary>
    private static TileCoords? ParseTileCoords(ReadOnlySpan<char> readName)
    {
        // Strip leading '@' if present
        if (readName.Length > 0 && readName[0] == '@')
        {
            readName = readName[1..];
        }

        Span<Range> parts = stackalloc Range[8];
        var count = readName.Split(parts, ':');
        if (count < 7)
        {
            return null;
        }

        var flowcell = new string(readName[parts[2]]);
        if (!int.TryParse(readName[parts[3]], out var lane))
        {
            return null;
        }

        if (!int.TryParse(readName[parts[4]], out var tile))
        {
            return null;
        }

        if (!int.TryParse(readName[parts[5]], out var x))
        {
            return null;
        }

        // Y field may have extra description after a space — take only the numeric prefix
        var ySpan = readName[parts[6]];
        var spaceIdx = ySpan.IndexOf(' ');
        if (spaceIdx >= 0)
        {
            ySpan = ySpan[..spaceIdx];
        }

        if (!int.TryParse(ySpan, out var y))
        {
            return null;
        }

        return new TileCoords(flowcell, lane, tile, x, y);
    }
}
