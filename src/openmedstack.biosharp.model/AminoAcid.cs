namespace OpenMedStack.BioSharp.Model
{
    using System;

    public record AminoAcid(char Letter, string Name)
    {
        public static implicit operator AminoAcid(char letter)
        {
            return letter switch
            {
                'A' => new AminoAcid('A', "Alanine"),
                'C' => new AminoAcid('C', "Cysteine"),
                'D' => new AminoAcid('D', "Aspartic Acid"),
                'E' => new AminoAcid('E', "Glutamic Acid"),
                'F' => new AminoAcid('F', "Phenylalanine"),
                'G' => new AminoAcid('G', "Glycine"),
                'L' => new AminoAcid('L', "Leucine"),
                'H' => new AminoAcid('H', "Histidine"),
                'I' => new AminoAcid('I', "Isoleucine"),
                'K' => new AminoAcid('K', "Lysine"),
                'M' => new AminoAcid('M', "Methionine"),
                'N' => new AminoAcid('N', "Asparagine"),
                'P' => new AminoAcid('P', "Proline"),
                'Q' => new AminoAcid('Q', "Glutamine"),
                'R' => new AminoAcid('R', "Arginine"),
                'S' => new AminoAcid('S', "Serine"),
                'T' => new AminoAcid('T', "Threonine"),
                'V' => new AminoAcid('V', "Valine"),
                'W' => new AminoAcid('W', "Tryptophan"),
                'Y' => new AminoAcid('Y', "Tyrosine"),
                '*' => new AminoAcid('*', "Nonsense"),
                _ => throw new ArgumentOutOfRangeException(nameof(letter))
            };
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Letter.ToString();
        }
    }
}