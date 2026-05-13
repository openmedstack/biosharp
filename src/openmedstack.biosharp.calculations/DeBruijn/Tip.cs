using System;

namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

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