namespace OpenMedStack.BioSharp.Model;

using System;
using System.Text.RegularExpressions;

/// <summary>
/// Defines the abstract HGVS description type.
/// </summary>
public abstract partial record HgvsDescription(ReferenceSequenceKind ReferenceSequenceKind)
{
    public static HgvsDescription Parse(string input)
    {
        if (SubstitutionRegex().IsMatch(input))
        {
            var match = SubstitutionRegex().Match(input);
            return new SubstitutionDescription(
                match.Groups["position"].Value,
                match.Groups["from"].Value[0],
                match.Groups["to"].Value[0],
                GetReferenceSequenceKind(match.Groups["kind"].Value[0]));
        }

        if (DeletionRegex().IsMatch(input))
        {
            var match = DeletionRegex().Match(input);
            var fromValue = match.Groups["from"].Value;
            // For single-position deletions (e.g. c.100del) the "to" group is absent;
            // treat it as equal to "from" so the model is always well-formed.
            var toValue = string.IsNullOrEmpty(match.Groups["to"].Value) ? fromValue : match.Groups["to"].Value;
            return new DeletionDescription(
                fromValue,
                toValue,
                GetReferenceSequenceKind(match.Groups["kind"].Value[0]),
                match.Groups["deleted"].Value);
        }

        if (DuplicationRegex().IsMatch(input))
        {
            var match = DuplicationRegex().Match(input);
            return new DuplicationDescription(
                match.Groups["from"].Value,
                match.Groups["to"].Value,
                GetReferenceSequenceKind(match.Groups["kind"].Value[0]),
                match.Groups["duplication"].Value);
        }

        if (InsertionRegex().IsMatch(input))
        {
            var match = InsertionRegex().Match(input);
            return new InsertionDescription(
                match.Groups["from"].Value,
                match.Groups["to"].Value,
                match.Groups["insertion"].Value,
                GetReferenceSequenceKind(match.Groups["kind"].Value[0]));
        }

        if (InsertionDeletionRegex().IsMatch(input))
        {
            var match = InsertionDeletionRegex().Match(input);
            return new DeletionInsertionDescription(
                match.Groups["from"].Value,
                match.Groups["to"].Value,
                match.Groups["deleted"].Value,
                string.IsNullOrWhiteSpace(match.Groups["insertion"].Value)
                    ? match.Groups["inserted"].Value
                    : match.Groups["insertion"].Value,
                GetReferenceSequenceKind(match.Groups["kind"].Value[0]));
        }

        if (UncertainPositionRegex().IsMatch(input))
        {
            var match = UncertainPositionRegex().Match(input);
            return new UncertainPositionDescription(
                match.Groups["position"].Value,
                GetReferenceSequenceKind(match.Groups["kind"].Value[0]));
        }

        throw new ArgumentException($"Cannot parse input: {input}", nameof(input));
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

    // Position pattern used in the regexes below supports:
    //   plain coding position:     100
    //   upstream (5' UTR):        -100
    //   downstream (3' UTR/stop): *100
    //   intronic offset:          100+1  or  100-1
    // Written as: [-*]?\d+(?:[+-]\d+)?

    [GeneratedRegex(@"(?<kind>[gomcnrp])\.(?<position>[-*]?\d+(?:[+-]\d+)?)\?", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex UncertainPositionRegex();

    [GeneratedRegex(@"(?<kind>[gomcnrp])\.(?<position>[-*]?\d+(?:[+-]\d+)?)(?<from>[A-Z])>(?<to>[A-Z])",
        RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex SubstitutionRegex();

    // The "_to" range is optional to support single-base deletions: c.100del
    [GeneratedRegex(@"^(?<kind>[gomcnrp])\.(?<from>[-*]?\d+(?:[+-]\d+)?)(?:_(?<to>[-*]?\d+(?:[+-]\d+)?))?del(?<deleted>[A-Z]+)?$",
        RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex DeletionRegex();

    [GeneratedRegex(@"^(?<kind>[gomcnrp])\.(?<from>[-*]?\d+(?:[+-]\d+)?)_(?<to>[-*]?\d+(?:[+-]\d+)?)dup(?<duplication>[A-Z]+)?$",
        RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex DuplicationRegex();

    [GeneratedRegex(@"^(?<kind>[gomcnrp])\.(?<from>[-*]?\d+(?:[+-]\d+)?)_(?<to>[-*]?\d+(?:[+-]\d+)?)ins(?<insertion>[A-Z]+)$",
        RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex InsertionRegex();

    [GeneratedRegex(
        @"^(?<kind>[gomcnrp])\.(?<from>[-*]?\d+(?:[+-]\d+)?)_(?<to>[-*]?\d+(?:[+-]\d+)?)(delins(?<insertion>[A-Z]+)|del(?<deleted>[A-Z]+)+ins(?<inserted>[A-Z]+))$",
        RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex InsertionDeletionRegex();
}
