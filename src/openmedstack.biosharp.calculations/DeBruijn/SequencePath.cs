namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

using System;

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