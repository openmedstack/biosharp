namespace OpenMedStack.BioSharp.Io.Sam
{
    using System.Collections.Generic;
    using System.Collections.Immutable;

    public enum CigarOp: byte
    {
        Match = 0,
        Insertion = 1,
        Deletion = 2,
        Skip = 3,
        SoftClip = 4,
        HardClip = 4,
        Padding = 6,
        Equal = 7,
        Difference = 8
    }

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
