using System.Collections.Generic;

namespace OpenMedStack.BioSharp.Model.Alignment;

/// <summary>
/// Represents a short-read alignment span used as input for read-backed phasing.
/// Carries the read's genomic interval and which variant positions it supports
/// as reference (false) or alternate (true) alleles.
/// </summary>
public sealed class ReadSpan
{
    /// <summary>Read identifier.</summary>
    public string Name { get; }

    /// <summary>1-based inclusive start position of the read on the reference.</summary>
    public int Start { get; }

    /// <summary>1-based inclusive end position of the read on the reference.</summary>
    public int End { get; }

    /// <summary>
    /// Positions (1-based) covered by this read, paired with a flag indicating
    /// whether the read supports the alternate allele (true) or the reference (false).
    /// </summary>
    public IReadOnlyList<(int Position, bool IsAlt)> Alleles { get; }

    public ReadSpan(string name, int start, int end, params (int Position, bool IsAlt)[] alleles)
    {
        Name = name;
        Start = start;
        End = end;
        Alleles = alleles;
    }

    /// <summary>Returns true if this read overlaps the given 1-based position.</summary>
    public bool Covers(int position) => position >= Start && position <= End;
}
