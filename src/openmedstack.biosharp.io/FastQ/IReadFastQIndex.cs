namespace OpenMedStack.BioSharp.Io.FastQ;

using System.Collections.Generic;
using System.Threading;

public interface IReadFastQIndex
{
    IAsyncEnumerable<(string key, BlockOffsetRecord offset)> Read(CancellationToken cancellationToken = default);
}