namespace OpenMedStack.BioSharp.Io.Bam;

public record BamIndex(SequenceIndex[] Content, ulong NumberOfUnmappedReads = 0);