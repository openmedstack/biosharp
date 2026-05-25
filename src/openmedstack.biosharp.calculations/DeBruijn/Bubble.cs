using System;
using OpenMedStack.BioSharp.Model;

namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

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
