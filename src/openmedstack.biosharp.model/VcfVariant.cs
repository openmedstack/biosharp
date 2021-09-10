namespace OpenMedStack.BioSharp.Model
{
    using System.Linq;

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
        public char Reference { get; init; }

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

        public static VcfVariant Parse(string line)
        {
            var parts = line.Split('\t');
            return new VcfVariant
            {
                Chromosome = parts[0],
                Position = int.Parse(parts[1]),
                MarkerIdentifiers = parts[2],
                Reference = parts[3][0],
                Alternate = parts[4],
                ErrorProbabilities = GetProbabilities(parts[5]),
                FailedFilter = new[] { parts[6] },
                AdditionalInformation = parts[7]
            };
        }

        private static int[] GetProbabilities(string part)
        {
            return part.Split('/').Select(p => p == "." ? 0 : int.Parse(p)).ToArray();
        }
    }
}
