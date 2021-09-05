namespace OpenMedStack.BioSharp.Model
{
    using System;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Defines the abstract HGVS description type.
    /// </summary>
    public abstract record HgvsDescription(ReferenceSequenceKind ReferenceSequenceKind)
    {
        private static readonly Regex SubstitutionRegex = new(
            "(?<kind>[gomcnrp])\\.(?<position>\\d+)(?<from>[A-Z])>(?<to>[A-Z])",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex DeletionRegex = new(
            "^(?<kind>[gomcnrp])\\.(?<from>\\d+)_(?<to>\\d+)del(?<deleted>[A-Z]+)?$",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex DuplicationRegex = new(
            "^(?<kind>[gomcnrp])\\.(?<from>\\d+)_(?<to>\\d+)dup(?<duplication>[A-Z]+)?$",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex InsertionRegex = new(
            "^(?<kind>[gomcnrp])\\.(?<from>\\d+)_(?<to>\\d+)ins(?<insertion>[A-Z]+)$",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex InsertionDeletionRegex = new(
            "^(?<kind>[gomcnrp])\\.(?<from>\\d+)_(?<to>\\d+)(delins(?<insertion>[A-Z]+)|del(?<deleted>[A-Z]+)+ins(?<inserted>[A-Z]+))$",
            RegexOptions.Compiled | RegexOptions.Singleline);

        public static HgvsDescription Parse(string input)
        {
            if (SubstitutionRegex.IsMatch(input))
            {
                var match = SubstitutionRegex.Match(input);
                return new SubstitutionDescription(
                    match.Groups["position"].Value,
                    match.Groups["from"].Value[0],
                    match.Groups["to"].Value[0],
                    GetReferenceSequenceKind(match.Groups["kind"].Value[0]));
            }

            if (DeletionRegex.IsMatch(input))
            {
                var match = DeletionRegex.Match(input);
                return new DeletionDescription(
                    match.Groups["from"].Value,
                    match.Groups["to"].Value,
                    GetReferenceSequenceKind(match.Groups["kind"].Value[0]),
                    match.Groups["deleted"].Value);
            }

            if (DuplicationRegex.IsMatch(input))
            {
                var match = DuplicationRegex.Match(input);
                return new DuplicationDescription(
                    match.Groups["from"].Value,
                    match.Groups["to"].Value,
                    GetReferenceSequenceKind(match.Groups["kind"].Value[0]),
                    match.Groups["duplication"].Value);
            }

            if (InsertionRegex.IsMatch(input))
            {
                var match = InsertionRegex.Match(input);
                return new InsertionDescription(
                    match.Groups["from"].Value,
                    match.Groups["to"].Value,
                    match.Groups["insertion"].Value,
                    GetReferenceSequenceKind(match.Groups["kind"].Value[0]));
            }

            if (InsertionDeletionRegex.IsMatch(input))
            {
                var match = InsertionDeletionRegex.Match(input);
                return new DeletionInsertionDescription(
                    match.Groups["from"].Value,
                    match.Groups["to"].Value,
                    match.Groups["deleted"].Value,
                    string.IsNullOrWhiteSpace(match.Groups["insertion"].Value)
                        ? match.Groups["inserted"].Value
                        : match.Groups["insertion"].Value,
                    GetReferenceSequenceKind(match.Groups["kind"].Value[0]));
            }

            throw new ArgumentException("Cannot parse input", nameof(input));
        }

        private static ReferenceSequenceKind GetReferenceSequenceKind(char kind)
        {
            return kind switch
            {
                'g' => ReferenceSequenceKind.LinearGenomic,
                'o' => ReferenceSequenceKind.CircularGenomic,
                'm' => ReferenceSequenceKind.Mitochondrial,
                'c' => ReferenceSequenceKind.CodingDna,
                'n' => ReferenceSequenceKind.NonCodingDna,
                'r' => ReferenceSequenceKind.Rna,
                'p' => ReferenceSequenceKind.Protein,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }
    }

    /// <summary>
    /// Defines the HGVS substitution description type.
    /// </summary>
    public record SubstitutionDescription(
        string Position,
        char From,
        char To,
        ReferenceSequenceKind ReferenceSequenceKind) : HgvsDescription(ReferenceSequenceKind);

    /// <summary>
    /// Defines the HGVS deletion description type.
    /// </summary>
    public record DeletionDescription(
        string From,
        string To,
        ReferenceSequenceKind ReferenceSequenceKind,
        string? Deletion = null) : HgvsDescription(ReferenceSequenceKind);

    /// <summary>
    /// Defines the HGVS duplication description type.
    /// </summary>
    public record DuplicationDescription(
        string From,
        string To,
        ReferenceSequenceKind ReferenceSequenceKind,
        string? Duplication = null) : HgvsDescription(ReferenceSequenceKind);

    /// <summary>
    /// Defines the HGVS insertion description type.
    /// </summary>
    public record InsertionDescription(
        string From,
        string To,
        string Insertion,
        ReferenceSequenceKind ReferenceSequenceKind) : HgvsDescription(ReferenceSequenceKind);

    /// <summary>
    /// Defines the HGVS deletion/insertion description type.
    /// </summary>
    public record DeletionInsertionDescription(
        string From,
        string To,
        string? Deleted,
        string Inserted,
        ReferenceSequenceKind ReferenceSequenceKind) : HgvsDescription(ReferenceSequenceKind);

    public enum ReferenceSequenceKind : byte
    {
        LinearGenomic,
        CircularGenomic,
        Mitochondrial,
        CodingDna,
        NonCodingDna,
        Rna,
        Protein
        /*
        g.	= linear genomic reference sequence
        o.	= circular genomic reference sequence
        m.	= mitochondrial reference (special case of a circular genomic reference sequence)
        c.	= coding DNA reference sequence (based on a protein coding transcript)
        n.	= non-coding DNA reference sequence (based on a transcript not coding for a protein)

        RNA

        r.	= RNA reference sequence

        protein

        p.	= protein reference sequence

            */
    }
}