namespace OpenMedStack.BioSharp.Io.Bam;

using Bcl;

public record BamIndex(SequenceIndex[] Content, ulong NumberOfUnmappedReads = 0);

public record SequenceIndex(Bin[] Content, ulong[] Offsets);

public record Bin(uint BinNumber, Chunk[] Chunks);

public record Chunk(BlockOffsetRecord Begin, BlockOffsetRecord End);