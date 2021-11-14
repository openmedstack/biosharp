namespace OpenMedStack.BioSharp.Io.Bcl;

using System.Threading.Tasks;

public class NoOffsetIndexReader : IBclIndexReader
{
    /// <inheritdoc />
    public int NumTiles { get; } = 1;

    /// <inheritdoc />
    public Task<BlockOffsetRecord> Get(int recordNumber)
    {
        return Task.FromResult(new BlockOffsetRecord(0, 0));
    }
}