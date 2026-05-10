namespace OpenMedStack.BioSharp.Io;

public record struct BlockOffsetRecord
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
        BlockAddress = blockAddress;
        BlockOffset = blockOffset;
    }

    public ulong BlockAddress { get; }

    public int BlockOffset { get; }

    public static implicit operator BlockOffsetRecord(ulong virtualFilePointer)
    {
        return new BlockOffsetRecord(virtualFilePointer);
    }

    public static implicit operator ulong(BlockOffsetRecord record)
    {
        return (record.BlockAddress << ShiftAmount) | (uint)record.BlockOffset;
    }

    public static explicit operator long(BlockOffsetRecord record)
    {
        return (long)((record.BlockAddress << ShiftAmount) | (uint)record.BlockOffset);
    }
}
