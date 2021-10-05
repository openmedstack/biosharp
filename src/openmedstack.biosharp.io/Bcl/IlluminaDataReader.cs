namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml.Serialization;
    using Model;
    using Model.Bcl;

    public class IlluminaDataReader
    {
        private readonly FileInfo? _runInfo;
        private readonly FileInfo? _sampleSheetInfo;
        private readonly DirectoryInfo _dataDir;
        private readonly DirectoryInfo _intensitiesDir;
        private readonly DirectoryInfo _baseCallDir;
        private readonly DirectoryInfo[] _baseCallLaneDirs;
        private readonly DirectoryInfo[] _laneDirs;
        private Run? _run;

        public IlluminaDataReader(DirectoryInfo runDir)
        {
            _runInfo = runDir.EnumerateFiles("RunInfo.xml", SearchOption.TopDirectoryOnly).SingleOrDefault();
            _dataDir = runDir.EnumerateDirectories("Data", SearchOption.TopDirectoryOnly).Single();
            _intensitiesDir = _dataDir.EnumerateDirectories("Intensities", SearchOption.TopDirectoryOnly).Single();
            _baseCallDir = _intensitiesDir.EnumerateDirectories("BaseCalls", SearchOption.TopDirectoryOnly).Single();
            _sampleSheetInfo = _baseCallDir.EnumerateFiles("SampleSheet.csv", SearchOption.TopDirectoryOnly).SingleOrDefault();
            _baseCallLaneDirs = _baseCallDir.EnumerateDirectories().Where(x => x.Name.StartsWith("L00")).ToArray();
            _laneDirs = _intensitiesDir.EnumerateDirectories().Where(x => x.Name.StartsWith("L00")).ToArray();
        }

        public Run RunInfo()
        {
            if (_run != null)
            {
                return _run;
            }

            if (_runInfo == null)
            {
                _run = new Run
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Date = DateTime.UtcNow.ToString("yyMMdd"),
                    Flowcell = Guid.NewGuid().ToString("N"),
                    Instrument = Guid.NewGuid().ToString("N"),
                    FlowcellLayout = new FlowcellLayout
                    {
                        LaneCount = (sbyte)_baseCallLaneDirs.Length,
                        TileSet = new TileSet
                        {
                            Tiles = new Tiles { Tile = _baseCallLaneDirs.SelectMany(GetTileSet).ToList() }
                        }
                    }
                };
                return _run;
            }

            var serializer = new XmlSerializer(typeof(RunInfo));
            using var runFile = File.OpenRead(_runInfo!.FullName);
            var info = serializer.Deserialize(runFile) as RunInfo;

            _run = info?.Run ?? throw new Exception("Could not read " + _runInfo.FullName);
            return _run;
        }

        private static List<string> GetTileSet(DirectoryInfo dir)
        {
            return dir.GetDirectories()
                .Where(d => d.Name.StartsWith('C'))
                .SelectMany(d => d.EnumerateFiles().Select(x => x.Name.Split('.')[0].Split('_')[2]))
                .Distinct()
                .ToList();
        }

        public async IAsyncEnumerable<(string index, Sequence[] sequences)> ReadSequences([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var runInfo = RunInfo();
            var readIndex =
                runInfo.Reads.Read.FindIndex(r => r.IsIndexedRead.Equals("Y", StringComparison.OrdinalIgnoreCase));
            var laneReaders = from dir in _baseCallLaneDirs
                              let lane = dir.Name[^1..]
                              let laneNo = int.Parse(lane.AsSpan())
                              let tiles = runInfo.FlowcellLayout?.TileSet?.Tiles == null ? GetTileSet(dir) :
                                  (from t in runInfo.FlowcellLayout.TileSet.Tiles.Tile
                                   where t.StartsWith(lane + '_')
                                   select t.Split('_')[1]).ToList()
                              from tile in tiles
                              let tileNo = int.Parse(tile.AsSpan())
                              let files = from d in dir.GetDirectories()
                                          orderby int.Parse(d.Name.Split('.')[0][1..])
                                          from f in d.GetFiles()
                                          where tiles.Contains(f.Name.Split('.')[0].Split('_')[2])
                                          select f
                              select new SampleReader(
                                  laneNo,
                                  laneNo,
                                  tileNo,
                                  new BclReader(
                                      files.ToList(),
                                      runInfo.Reads.Read.OrderBy(x => x.Number).Select(r => r.NumCycles).ToArray(),
                                      new BclQualityEvaluationStrategy(2),
                                      false),
                                  GetPositionReader(laneNo, tileNo, lane));
            foreach (var reader in laneReaders.AsParallel())
            {
                var enumerator = reader.PositionReader.GetAsyncEnumerator(cancellationToken);
                await using var positionEnumerator = enumerator.ConfigureAwait(false);
                await foreach (var data in ((IAsyncEnumerable<BclData>)reader.Reader).ConfigureAwait(false))
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        throw new Exception("Could not read position for sequence read");
                    }

                    var headerStart =
                        $"{runInfo.Instrument}:{runInfo.Number}:{runInfo.Flowcell}:{reader.Lane}:{reader.Tile}:{enumerator.Current.XCoordinate}:{enumerator.Current.YCoordinate}";
                    var sequences = new Sequence[data.Bases.Length];
                    var index = readIndex == -1 ? reader.Sample.ToString() : Encoding.ASCII.GetString(data.Bases[readIndex]);
                    for (var i = 0; i < data.Bases.Length; i++)
                    {
                        var id =
                            $"{headerStart} {reader.Sample}:{i}:N:0:{index}";
                        sequences[i] = new Sequence(
                            id,
                            data.Bases[i],
                            Array.ConvertAll(data.Qualities[i], b => (byte)(b + 33)),
                            i == readIndex);
                    }

                    yield return (index, sequences);
                }

                await reader.Reader.DisposeAsync().ConfigureAwait(false);
            }
        }

        private IAsyncEnumerable<IPositionalData> GetPositionReader(int lane, int tile, string sample)
        {
            var positionFile = _laneDirs.Single(d => d.Name[^1..].Equals(lane.ToString()))
                .EnumerateFiles()
                .Single(f => f.Name.StartsWith($"s_{sample}_{tile}"));
            return Path.GetExtension(positionFile.Name) switch
            {
                ".clocs" => new ClocsFileReader(positionFile, lane, tile),
                ".locs" => new LocsFileReader(positionFile, lane, tile),
                _ => throw new NotSupportedException("Unsupported file extension " + Path.GetExtension(positionFile.Name))
            };
        }
    }

    public class SampleReader
    {
        public SampleReader(int lane, int sample, int tile, BclReader reader, IAsyncEnumerable<IPositionalData> positionReader)
        {
            Lane = lane;
            Sample = sample;
            Tile = tile;
            Reader = reader;
            PositionReader = positionReader;
        }

        public int Lane { get; }

        public int Sample { get; }

        public int Tile { get; }
        public BclReader Reader { get; }
        public IAsyncEnumerable<IPositionalData> PositionReader { get; }
    }
}
