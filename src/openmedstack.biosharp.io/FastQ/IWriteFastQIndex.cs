namespace OpenMedStack.BioSharp.Io.FastQ;

using System.Threading;
using System.Threading.Tasks;

public interface IWriteFastQIndex
{
    Task Write(string key, BlockOffsetRecord blockOffset, CancellationToken cancellationToken = default);
}