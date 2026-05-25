using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Orchestrates structural variant detection by combining bubble analysis and tip analysis.
/// Converts graph-based findings into LocalVariantResult records compatible with the
/// existing variant caller pipeline.
/// </summary>
public static class StructuralVariantDetector
{
    /// <summary>
    /// Analyzes a De Bruijn graph to detect structural variants relative to a reference
    /// sequence. Produces LocalVariantResult records.
    /// </summary>
    /// <param name="graph">The assembled De Bruijn graph from reads.</param>
    /// <param name="reference">The reference sequence this graph was built relative to.</param>
    /// <param name="chromosome">Chromosome name for variant output.</param>
    /// <param name="refStart">Starting genomic position of the reference in the larger genome.</param>
    /// <returns>StructuralVariantAnalysis with variant calls from the graph.</returns>
    public static async Task<StructuralVariantAnalysis> AnalyzeGraph(
        DeBruijnGraph graph,
        string reference,
        string chromosome,
        int refStart)
    {
        var variants = new List<LocalVariantResult>();

        // Step 1: Detect bubbles (divergent paths)
        var bubbles = await BubbleFinder.FindBubbles(graph, graph.K);
        foreach (var bubble in bubbles)
        {
            var bubbleVariants = AnalyzeBubble(bubble, graph, reference, chromosome, refStart);
            variants.AddRange(bubbleVariants);
        }

        // Step 2: Detect tips (dead-end branches, potential insertions)
        var longTipThreshold = graph.K * 2;
        var tips = await TipFinder.FindTips(graph, longTipThreshold);
        foreach (var tip in tips)
        {
            if (tip.IsLongTip)
            {
                var tipVariant = AnalyzeTip(tip, reference, chromosome, refStart);
                if (tipVariant != null)
                {
                    variants.Add(tipVariant);
                }
            }
        }

        // Step 3: Sort variants by position
        variants.Sort((a, b) => a.Position.CompareTo(b.Position));

        return new StructuralVariantAnalysis(variants.ToArray());
    }

    /// <summary>
    /// Analyzes a bubble to produce variant calls.
    /// Compares alt paths against the reference to determine SNP, insertion, deletion, or inversion.
    /// IMPORTANT: Reference path is identified by alignment to reference sequence,
    /// not by coverage, to correctly classify insertions vs deletions.
    /// </summary>
    private static List<LocalVariantResult> AnalyzeBubble(
        Bubble bubble,
        DeBruijnGraph? graph,
        string reference,
        string chromosome,
        int refStart)
    {
        var variants = new List<LocalVariantResult>();

        if (bubble.Paths.Length < 2)
        {
            return variants;
        }

        // Identify reference path by alignment to reference sequence, then coverage
        var scoredPaths = bubble.Paths.Select(p => new
        {
            Path = p,
            AlignScore = ScoreAlignmentToRef(p.Sequence, reference)
        }).ToList();

        var sortedPaths = scoredPaths
            .OrderByDescending(x => x.AlignScore)
            .ThenByDescending(x => x.Path.Coverage)
            .ToList();

        var refPathStr = sortedPaths[0].Path.Sequence;
        var altPathObjects = sortedPaths.Skip(1).ToList();

        if (altPathObjects.Count == 0)
        {
            return variants;
        }

        var position = FindPositionInReference(refPathStr, reference, refStart);

        if (position < 0)
        {
            return variants;
        }

        var altSequence = ConsensusFromAltPaths(altPathObjects.Select(x => x.Path).ToList());
        var altCoverage = altPathObjects.Sum(x => x.Path.Coverage);
        var refCoverage = sortedPaths[0].Path.Coverage;
        var totalCoverage = altCoverage + refCoverage;

        if (totalCoverage == 0 || altCoverage == 0)
        {
            return variants;
        }

        var lengthDiff = altSequence.Length - refPathStr.Length;
        var variant = new LocalVariantResult
        {
            Chromosome = chromosome,
            Position = position,
            Reference = refPathStr,
            Alternate = altSequence,
            QuantitativeQuality = ComputeQuality(altCoverage, totalCoverage),
            Depth = totalCoverage
        };

        // Apply RepetitivenessAnalyzer confidence scoring to the bubble
        // Build a simple k-mer count map from the bubble paths
        var kmerCounts = BuildKmerCounts(bubble, graph?.K ?? 21);
        _ = RepetitivenessAnalyzer.AnalyzeBubble(bubble, kmerCounts);
        variant.BubbleConfidence = RepetitivenessAnalyzer.Analyze(bubble, kmerCounts);

        // Classify variant type
        if (lengthDiff == 0)
        {
            // Same length - could be SNP or inversion
            if (altSequence != refPathStr)
            {
                // Check for inversion (reverse complement match)
                var altRevComp = ReverseComplement(altSequence);
                if (altRevComp == refPathStr)
                {
                    variant.IsStructuralVariant = true;
                    variant.SvType = SvType.Inversion;
                    variant.EndPosition = position + refPathStr.Length;
                }
                else
                {
                    // Substitution/SNP - not a structural variant
                    variant.IsStructuralVariant = false;
                }
            }
        }
        else if (Math.Abs(lengthDiff) >= 5 ||
            Math.Abs(lengthDiff) >= (int)Math.Ceiling(refPathStr.Length * 0.1))
        {
            // Significant length difference - structural variant
            variant.IsStructuralVariant = true;
            variant.EndPosition = position + Math.Max(refPathStr.Length, altSequence.Length);

            if (lengthDiff > 0)
                // Alt is longer - insertion
            {
                variant.SvType = SvType.Insertion;
            }
            else
                // Alt is shorter - deletion
            {
                variant.SvType = SvType.Deletion;
            }
        }
        else
        {
            // Small length diff (< 5 bp or < 10% of ref) - treat as indel, not SV
            if (altSequence != refPathStr)
            {
                variant.IsStructuralVariant = false;
            }
        }

        // Set assembly info
        variant.AssemblyInfo = new AssemblyInfo(totalCoverage, altSequence.Length, altPathObjects.Count);

        variants.Add(variant);

        return variants;
    }

    /// <summary>
    /// Analyzes a tip to see if it represents an insertion variant.
    /// </summary>
    private static LocalVariantResult? AnalyzeTip(Tip tip, string reference, string chromosome, int refStart)
    {
        if (tip.Length < 5) // minimum size for a meaningful variant
        {
            return null;
        }

        // The tip represents bases not in the reference - potential insertion
        const int altCoverage = 1; // tip coverage would come from read depth, use 1 as default
        const int refCoverage = 1;

        // Try to find where this tip aligns to the reference
        var alignPos = FindPositionInReference(tip.Sequence, reference, refStart);

        if (alignPos < 0)
            // Tip doesn't align to reference - still report as insertion at approximate position
        {
            alignPos = refStart;
        }

        var variant = new LocalVariantResult
        {
            Chromosome = chromosome,
            Position = alignPos,
            Reference = string.Empty, // insertion: reference has nothing extra
            Alternate = tip.Sequence,
            QuantitativeQuality = ComputeQuality(altCoverage, altCoverage + refCoverage),
            Depth = altCoverage + refCoverage
        };

        // Long tips are structural variant insertions
        if (tip.Length >= 50)
        {
            variant.IsStructuralVariant = true;
            variant.SvType = SvType.Insertion;
            variant.EndPosition = alignPos + tip.Sequence.Length;
            variant.AssemblyInfo = new AssemblyInfo(
                tip.Length,
                tip.Sequence.Length);
        }

        return variant;
    }

    /// <summary>
    /// Computes a Phred-like quality score from alt/read ratio.
    /// Returns 10-40 range.
    /// </summary>
    private static int ComputeQuality(int altCount, int totalCount)
    {
        if (totalCount == 0)
        {
            return 0;
        }

        if (altCount == 0)
        {
            return 0;
        }

        var ratio = (double)altCount / totalCount;
        // Simple Phred: Q = -10 * log10(1-ratio), clamped to [10, 40]
        var errorProb = 1.0 - ratio;
        if (errorProb >= 1.0)
        {
            return 0;
        }

        if (errorProb <= 0.0)
        {
            return 40;
        }

        var q = -10.0 * Math.Log10(errorProb);
        return Math.Clamp((int)q, 10, 40);
    }

    /// <summary>
    /// Finds the best approximate position of a sequence in the reference.
    /// Uses a simple sliding window match.
    /// </summary>
    private static int FindPositionInReference(string seq, string reference, int refStart)
    {
        if (string.IsNullOrEmpty(seq) || seq.Length > reference.Length)
        {
            return -1;
        }

        var bestPos = -1;
        var bestMatches = 0;

        var window = Math.Min(seq.Length, reference.Length);
        for (var i = 0; i <= reference.Length - window; i++)
        {
            var matches = 0;
            for (var j = 0; j < window; j++)
            {
                if (char.ToUpper(reference[i + j]) == char.ToUpper(seq[j]))
                {
                    matches++;
                }
            }

            if (matches > bestMatches)
            {
                bestMatches = matches;
                bestPos = refStart + i;
            }
        }

        // Require at least 60% match for a valid position
        if (bestPos < 0 || bestMatches < window * 0.6)
        {
            return -1;
        }

        return bestPos;
    }

    /// <summary>
    /// Creates a consensus sequence from multiple alt paths.
    /// Uses majority vote at each position.
    /// </summary>
    private static string ConsensusFromAltPaths(List<SequencePath> altPaths)
    {
        if (altPaths.Count == 0)
        {
            return string.Empty;
        }

        if (altPaths.Count == 1)
        {
            return altPaths[0].Sequence;
        }

        // Pad all sequences to the same length (use max length)
        var maxLen = altPaths.Max(p => p.Sequence.Length);
        var consensus = new char[maxLen];
        var baseCounts = new int[4]; // A, C, G, T

        for (var i = 0; i < maxLen; i++)
        {
            Array.Fill(baseCounts, 0);
            for (var j = 0; j < altPaths.Count; j++)
            {
                if (i < altPaths[j].Sequence.Length)
                {
                    var baseChar = char.ToUpper(altPaths[j].Sequence[i]);
                    switch (baseChar)
                    {
                        case 'A': baseCounts[0] += altPaths[j].Coverage; break;
                        case 'C': baseCounts[1] += altPaths[j].Coverage; break;
                        case 'G': baseCounts[2] += altPaths[j].Coverage; break;
                        case 'T': baseCounts[3] += altPaths[j].Coverage; break;
                        default: baseCounts[0]++; break; // N = ambiguous, default to A
                    }
                }
            }

            // Pick the base with highest coverage
            var maxIdx = 0;
            for (var b = 1; b < baseCounts.Length; b++)
            {
                if (baseCounts[b] > baseCounts[maxIdx])
                {
                    maxIdx = b;
                }
            }

            consensus[i] = new[] { 'A', 'C', 'G', 'T' }[maxIdx];
        }

        return new string(consensus);
    }

    /// <summary>
    /// Scores how well a path sequence aligns to the reference.
    /// Returns a match count (higher is better). Used to identify the reference path in a bubble.
    /// Aligns using the same window-based approach as FindPositionInReference but
    /// returns the raw match score rather than position.
    /// </summary>
    private static int ScoreAlignmentToRef(string seq, string reference)
    {
        if (string.IsNullOrEmpty(seq) || seq.Length > reference.Length)
        {
            return 0;
        }

        var bestScores = 0;

        var window = Math.Min(seq.Length, reference.Length);
        for (var i = 0; i <= reference.Length - window; i++)
        {
            var matches = 0;
            for (var j = 0; j < window; j++)
            {
                if (char.ToUpper(reference[i + j]) == char.ToUpper(seq[j]))
                {
                    matches++;
                }
            }

            if (matches > bestScores)
            {
                bestScores = matches;
            }
        }

        return bestScores;
    }

    /// <summary>
    /// Returns the reverse complement of a DNA sequence.
    /// Used to detect inversions (where the alt path is the reverse complement of ref).
    /// </summary>
    private static string ReverseComplement(string sequence)
    {
        if (string.IsNullOrEmpty(sequence))
        {
            return sequence;
        }

        var complement = new char[sequence.Length];
        for (var i = 0; i < sequence.Length; i++)
        {
            var baseChar = char.ToUpper(sequence[sequence.Length - 1 - i]);
            complement[i] = baseChar switch
            {
                'A' => 'T',
                'T' => 'A',
                'C' => 'G',
                'G' => 'C',
                _ => baseChar
            };
        }

        return new string(complement);
    }

    /// <summary>
    /// Builds a k-mer count dictionary from all paths in a bubble.
    /// Extracts all k-length substrings from each path sequence.
    /// </summary>
    private static Dictionary<string, int> BuildKmerCounts(Bubble bubble, int k)
    {
        var counts = new Dictionary<string, int>();
        foreach (var path in bubble.Paths)
        {
            if (path == null || string.IsNullOrEmpty(path.Sequence))
            {
                continue;
            }

            var seq = path.Sequence.ToUpper();
            if (seq.Length < k)
            {
                counts.TryGetValue(seq, out var c);
                counts[seq] = c + path.Coverage;
                continue;
            }

            for (var i = 0; i <= seq.Length - k; i++)
            {
                var kmer = seq.Substring(i, k);
                counts.TryGetValue(kmer, out var existing);
                counts[kmer] = existing + path.Coverage;
            }
        }

        return counts;
    }
}
