namespace OpenMedStack.BioSharp.Io.Sam
{
    using System.Collections.Generic;
    using System.Collections.Immutable;

    public record SamDefinition
    {
        public SamDefinition(
            FileMetadata hd,
            IEnumerable<ReferenceSequence> sq,
            ReadGroup rg,
            Program pg,
            IEnumerable<AlignmentSection> alignmentSections)
        {
            Hd = hd;
            Sq = ImmutableArray<ReferenceSequence>.Empty.AddRange(sq);
            Rg = rg;
            Pg = pg;
            AlignmentSections = ImmutableArray<AlignmentSection>.Empty.AddRange(alignmentSections);
        }

        public FileMetadata Hd { get; }

        public ImmutableArray<ReferenceSequence> Sq { get; }

        public ReadGroup Rg { get; }

        public Program Pg { get; }

        public ImmutableArray<AlignmentSection> AlignmentSections { get; }
    }
}
