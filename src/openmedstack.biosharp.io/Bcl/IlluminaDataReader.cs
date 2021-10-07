namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml.Serialization;
    using Model;
    using Model.Bcl;

    public class IlluminaDataReader
    {
        private static readonly Regex LanefolderMatch = new("L(?<lane>\\d{3})", RegexOptions.Compiled);
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
            _baseCallLaneDirs = _baseCallDir.EnumerateDirectories().Where(x => LanefolderMatch.IsMatch(x.Name)).ToArray();
            _laneDirs = _intensitiesDir.EnumerateDirectories().Where(x => LanefolderMatch.IsMatch(x.Name)).ToArray();
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
            var laneReaders = CreateLaneReaders(runInfo).ToList();
            foreach (var reader in laneReaders.AsParallel())
            {
                await foreach (var p in ReadBclData(reader, runInfo, readIndex, cancellationToken).ConfigureAwait(false))
                {
                    yield return p;
                }

                await reader.Reader.DisposeAsync().ConfigureAwait(false);
            }
        }

        private IEnumerable<SampleReader> CreateLaneReaders(Run runInfo)
        {
            return from dir in _baseCallLaneDirs
                   let lane = LanefolderMatch.Match(dir.Name).Groups["lane"].Value.Trim('0')
                   let laneNo = int.Parse(lane.AsSpan())
                   let tiles = runInfo.FlowcellLayout?.TileSet?.Tiles == null ? GetTileSet(dir) :
                       (from t in runInfo.FlowcellLayout.TileSet.Tiles.Tile
                        where t.StartsWith(lane + '_')
                        select t.Split('_')[1]).ToList()
                   let cycleDirs = dir.GetDirectories().Length == 0 ? new[] { dir } : dir.EnumerateDirectories("C*", SearchOption.TopDirectoryOnly)
                   let files = GetReadFileInfos(cycleDirs, tiles).ToList()
                   let tileFiles = files.Any(file => file.Name.Split('.')[0].Contains('_'))
                   from tile in tileFiles ? tiles : tiles.Take(1)
                   let tileNo = int.Parse(tile.AsSpan())

                   select new SampleReader(
                       laneNo,
                       laneNo,
                       tileNo,
                       new BclReader(
                           files,
                           runInfo.Reads.Read.OrderBy(x => x.Number).Select(r => r.NumCycles).ToArray(),
                           new BclQualityEvaluationStrategy(2),
                           false),
                       GetPositionReader(laneNo, tileNo, lane));
        }

        private static IEnumerable<FileInfo> GetReadFileInfos(IEnumerable<DirectoryInfo> cycleDirs, List<string> tiles)
        {
            return from d in cycleDirs
                orderby int.Parse(d.Name.Split('.')[0][1..])
                from f in d.GetFiles("*.bgzf").Concat(d.GetFiles("*.gz")).Concat(d.GetFiles("*.bcl"))
                let fileNameWithoutExtension = f.Name.Split('.')[0]
                where !fileNameWithoutExtension.Contains('_') || tiles.Contains(fileNameWithoutExtension.Split('_')[2])
                orderby f.Name
                select f;
        }

        private static async IAsyncEnumerable<(string index, Sequence[] sequences)> ReadBclData(
            SampleReader reader,
            Run runInfo,
            int readIndex,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var enumerator = reader.PositionReader.GetAsyncEnumerator(cancellationToken);
            await using var positionEnumerator = enumerator.ConfigureAwait(false);
            var enumerable = (IAsyncEnumerable<BclData>)reader.Reader;
            await foreach (var data in enumerable.ConfigureAwait(false))
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    throw new Exception("Could not read position for sequence read");
                }

                var headerStart =
                    $"{runInfo.Instrument}:{runInfo.Number}:{runInfo.Flowcell}:{reader.Lane}:{reader.Tile}:{enumerator.Current.XCoordinate}:{enumerator.Current.YCoordinate}";
                var sequences = new Sequence[data.Bases.Length];
                var index = readIndex == -1
                    ? reader.Sample.ToString()
                    : Encoding.ASCII.GetString(data.Bases[readIndex]);
                for (var i = 0; i < data.Bases.Length; i++)
                {
                    var id = $"{headerStart} {reader.Sample}:{i}:N:0:{index}";
                    sequences[i] = new Sequence(
                        id,
                        data.Bases[i],
                        Array.ConvertAll(data.Qualities[i], b => (byte)(b + 33)),
                        runInfo.Reads.Read[i].IsIndexedRead == "Y");
                }

                yield return (index, sequences);
            }
        }

        private IAsyncEnumerable<IPositionalData> GetPositionReader(int lane, int tile, string sample)
        {
            var files = _laneDirs.Single(d => d.Name[^1..].Equals(lane.ToString()))
                .GetFiles();
            var positionFile = files.SingleOrDefault(f => f.Name.StartsWith($"s_{sample}_{tile}"))
                ?? files.Single(f => f.Name.StartsWith($"s_{sample}"));
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
