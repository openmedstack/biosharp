namespace OpenMedStack.BioSharp.Model.Alignment;

/// <summary>
/// Helper class to accumulate CIGAR operation counts before merging.
/// </summary>
internal class CigarOpCount
{
    public int Count { get; }
    public char Op { get; }

    public CigarOpCount(int count, char op)
    {
        Count = count;
        Op = op;
    }
}