namespace OpenMedStack.BioSharp.Model;

public readonly struct BasePair
{
    public BasePair(char letter, char errorProbability = (char)0)
    {
        Letter = letter;
        ErrorProbability = errorProbability;
    }

    public char Letter { get; }

    public char ErrorProbability { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Letter}/{ErrorProbability}";
    }
}
