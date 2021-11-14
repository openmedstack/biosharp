namespace OpenMedStack.BioSharp.Io.Bcl;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class FileStuctureTileIndex : IAsyncEnumerable<TileIndexRecord>
{
    private readonly IEnumerable<int> _tiles;

    public FileStuctureTileIndex(IEnumerable<int> tiles)
    {
        _tiles = tiles.ToList();
    }

    /// <inheritdoc />
    public async IAsyncEnumerator<TileIndexRecord> GetAsyncEnumerator(
        CancellationToken cancellationToken = new ())
    {
        await Task.Yield();
        foreach (var tile in _tiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new TileIndexRecord(tile, int.MaxValue, 0, 0);
        }
    }
}