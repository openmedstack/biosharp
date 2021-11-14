namespace OpenMedStack.BioSharp.Io.Bcl;

public record TileIndexRecord(
    int Tile,
    int NumClustersInTile,
    int IndexOfFirstClusterInTile,
    int ZeroBasedTileNumber);