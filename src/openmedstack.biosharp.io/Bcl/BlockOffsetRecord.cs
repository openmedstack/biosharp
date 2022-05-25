namespace OpenMedStack.BioSharp.Io.Bcl;

public record BlockOffsetRecord
{
    private const int ShiftAmount = 16;
    private const int OffsetMask = 0xffff;
    private const long AddressMask = 0xFFFFFFFFFFFFL;

    public BlockOffsetRecord(ulong virtualFilePointer)
        : this((virtualFilePointer >> ShiftAmount) & AddressMask, (int)(virtualFilePointer & OffsetMask))
    {
    }

    public BlockOffsetRecord(ulong blockAddress, int blockOffset)
    {
        BlockAddress = (long)blockAddress;
        BlockOffset = blockOffset;
    }

    public long BlockAddress { get; }
    public int BlockOffset { get; }

    public static implicit operator BlockOffsetRecord(ulong virtualFilePointer)
    {
        return new BlockOffsetRecord(virtualFilePointer);
    }
}
