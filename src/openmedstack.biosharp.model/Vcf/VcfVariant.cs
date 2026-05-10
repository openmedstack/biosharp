namespace OpenMedStack.BioSharp.Model.Vcf;

using System;

/// <summary>
/// Defines the VcfVariant type.
/// </summary>
public record VcfVariant
{
    /// <summary>
    /// Gets or sets the chromosome
    /// </summary>
    public string Chromosome { get; init; } = null!;

    /// <summary>
    /// <para>Gets or sets the genome coordinate of the first base in the variant.</para>
    /// <para>Within a chromosome, VCF records are sorted in order of increasing position.</para>
    /// </summary>
    public int Position { get; init; }

    /// <summary>
    /// Gets or sets a semicolon-separated list of marker identifiers.
    /// </summary>
    public string MarkerIdentifiers { get; init; } = null!;

    /// <summary>
    /// Gets or sets the reference allele expressed as a sequence of one or more A/C/G/T nucleotides (e.g. "A" or "AAC")
    /// </summary>
    public string Reference { get; init; } = null!;

    /// <summary>
    /// <para>Gets or sets the alternate allele expressed as a sequence of one or more A/C/G/T nucleotides (e.g. "A" or "AAC").</para>
    /// <para>If there is more than one alternate alleles, the field should be a comma-separated list of alternate alleles.</para>
    /// </summary>
    public string Alternate { get; init; } = null!;

    /// <summary>
    /// Gets or sets the probability that the ALT allele is incorrectly specified, expressed on the the phred scale (-10log10(probability)).
    /// </summary>
    public int[] ErrorProbabilities { get; init; } = null!;

    /// <summary>
    /// Gets or sets either "PASS" or an array of failed quality control filters.
    /// </summary>
    public string[] FailedFilter { get; init; } = null!;

    /// <summary>
    /// Gets or sets additional information (no white space, tabs, or semi-colons permitted).
    /// </summary>
    public string AdditionalInformation { get; init; } = null!;

    public static VcfVariant? Parse(string line)
    {
        try
        {
            var span = line.AsSpan();
            Span<Range> ranges = stackalloc Range[8];
            var count = span.Split(ranges, '\t');
            return new VcfVariant
            {
                Chromosome = new string(span[ranges[0]]),
                Position = int.Parse(span[ranges[1]]),
                MarkerIdentifiers = new string(span[ranges[2]]),
                Reference = new string(span[ranges[3]]),
                Alternate = new string(span[ranges[4]]),
                ErrorProbabilities = GetProbabilities(span[ranges[5]]),
                FailedFilter = count > 6 ? [new string(span[ranges[6]])] : [],
                AdditionalInformation = count > 7 ? new string(span[ranges[7]]) : ""
            };
        }
        catch (Exception)
        {
//            throw new FormatException($"Failed to parse VCF line: {line}", ex);
            return null;
        }
    }

    private static int[] GetProbabilities(ReadOnlySpan<char> part)
    {
        Span<Range> ranges = stackalloc Range[8];
        var count = part.Split(ranges, '/');
        var result = new int[count];
        for (var i = 0; i < count; i++)
        {
            var segment = part[ranges[i]];
            result[i] = segment.Length == 1 && segment[0] == '.' ? 0 : (int)Math.Round(float.Parse(segment));
        }
        return result;
    }
}
