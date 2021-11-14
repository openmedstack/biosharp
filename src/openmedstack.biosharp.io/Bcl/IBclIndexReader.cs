namespace OpenMedStack.BioSharp.Io.Bcl;

using System.Threading.Tasks;

public interface IBclIndexReader
{
    int NumTiles { get; }
    Task<BlockOffsetRecord> Get(int recordNumber);
}