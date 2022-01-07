//namespace OpenMedStack.BioSharp.Io.Bam;

//public record BamIndex
//{
//    /*
//         Index	

//A 1-by-N array of structures, where N is the number of reference sequences in the corresponding BAM file. Each structure contains the following fields:

//    BinID — Array of bin IDs for one reference sequence.

//    BGZFOffsetStart — Offset in the BAM file to the start of the first BGZF block where alignment records associated with the corresponding BinID are stored.

//    BGZFOffsetEnd — Offset in the BAM file to the start of the last BGZF block where alignment records associated with the corresponding BinID are stored.

//    DataOffsetStart — Offset in the decompressed data block to the start of where alignment records associated with the corresponding BinID are stored.

//    DataOffsetEnd — Offset in the decompressed data block to the end of where alignment records associated with the corresponding BinID are stored.

//    LinearBGZFOffset — Offset in the BAM file to the first alignment in the corresponding 16384 bp interval.

//    LinearDataOffset — Offset in the decompressed data file to the first alignment in the corresponding 16384 bp interval.
//         */
//    public int[] BinId { get; init; } = null!;
//    public int BGZFOffsetStart { get; init; }
//    public int BGZFOffsetEnd { get; init; }
//    public int DataOffsetStart { get; init; }
//    public int DataOffsetEnd { get; init; }
//    public int LinearBGZFOffset { get; init; }
//    public int LinearDataOffset { get; init; }
//}