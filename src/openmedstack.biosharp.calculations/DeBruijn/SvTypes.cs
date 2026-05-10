namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

using System;
using Alignment;

public class SequencePath
{
    public SequencePath(string sequence, int coverage = 1)
    {
        Sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));
        Coverage = coverage;
    }

    public string Sequence { get; }
    public int Coverage { get; }

    public override string ToString()
    {
        return $"[cov:{Coverage}] {Sequence}";
    }
}

public class Bubble
{
    public Bubble(string startNode, string endNode, SequencePath[] paths)
    {
        StartNode = startNode;
        EndNode = endNode;
        Paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public string StartNode { get; }
    public string EndNode { get; }
    public SequencePath[] Paths { get; }

    /// <summary>
    /// Confidence level for this bubble after repetitiveness analysis.
    /// Defaults to High; set via RepetitivenessAnalyzer.AnalyzeBubble().
    /// </summary>
    public BubbleConfidence Confidence { get; set; } = BubbleConfidence.High;
}

public class Tip
{
    public Tip(string sequence, int length, bool isLongTip)
    {
        Sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));
        Length = length;
        IsLongTip = isLongTip;
    }

    public string Sequence { get; }
    public int Length { get; }
    public bool IsLongTip { get; }
}

/// <summary>
/// Result of structural variant analysis - bubbles, tips, and variant calls.
/// </summary>
public class StructuralVariantAnalysis
{
    public StructuralVariantAnalysis(LocalVariantResult[] variants)
    {
        Variants = variants;
    }

    public LocalVariantResult[] Variants { get; }
}
