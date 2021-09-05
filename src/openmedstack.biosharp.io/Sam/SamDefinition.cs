namespace OpenMedStack.BioSharp.Io.Sam
{
    using System.Collections.Generic;
    using System.Collections.Immutable;

    public record SamDefinition
    {
        public SamDefinition(FileMetadata hd, IEnumerable<ReferenceSequence> sq, ReadGroup rg, Program pg, IEnumerable<AlignmentSection> alignmentSections)
        {
            Hd = hd;
            Sq = ImmutableArray<ReferenceSequence>.Empty.AddRange(sq);
            Rg = rg;
            Pg = pg;
            AlignmentSections = ImmutableArray<AlignmentSection>.Empty.AddRange(alignmentSections);
        }

        public FileMetadata Hd { get; init; }

        public ImmutableArray<ReferenceSequence> Sq { get; init; }

        public ReadGroup Rg { get; init; }

        public Program Pg { get; init; }

        public ImmutableArray<AlignmentSection> AlignmentSections { get; init; }
    }
}