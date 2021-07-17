namespace OpenMedStack.BioSharp.Io.Sam
{
    using System.Collections.Immutable;

    public record SamDefinition
    {
        public FileMetadata Hd { get; init; }

        public ImmutableArray<ReferenceSequence> Sq { get; init; }

        public ReadGroup Rg { get; init; }

        public Program Pg { get; init; }

        public ImmutableArray<AlignmentSection> AlignmentSections { get; init; }
    }
}