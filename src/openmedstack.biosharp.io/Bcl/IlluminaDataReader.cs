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
    using Model.Bcl;

    public class IlluminaDataReader
    {
        private readonly ReadStructure? _readStructure;
        private static readonly Regex LaneFolderMatch = new("L(?<lane>\\d{3})", RegexOptions.Compiled);
        private static readonly Regex TileNumberMatch = new("(?<lane>(\\d+)_)?(?<tile>\\d+)", RegexOptions.Compiled);
        private readonly FileInfo? _runInfo;
        private readonly FileInfo? _sampleSheetInfo;
        private readonly DirectoryInfo _dataDir;
        private readonly DirectoryInfo _intensitiesDir;
        private readonly DirectoryInfo _baseCallDir;
        private readonly DirectoryInfo[] _baseCallLaneDirs;
        private readonly DirectoryInfo[] _laneDirs;
        private Run? _run;

        public IlluminaDataReader(DirectoryInfo runDir, ReadStructure? readStructure = null)
        {
            _readStructure = readStructure;
            _runInfo = runDir.EnumerateFiles("RunInfo.xml", SearchOption.TopDirectoryOnly).SingleOrDefault();
            _dataDir = runDir.EnumerateDirectories("Data", SearchOption.TopDirectoryOnly).Single();
            _intensitiesDir = _dataDir.EnumerateDirectories("Intensities", SearchOption.TopDirectoryOnly).Single();
            _baseCallDir = _intensitiesDir.EnumerateDirectories("BaseCalls", SearchOption.TopDirectoryOnly).Single();
            _sampleSheetInfo = _baseCallDir.EnumerateFiles("SampleSheet.csv", SearchOption.TopDirectoryOnly).SingleOrDefault();
            _baseCallLaneDirs = _baseCallDir.EnumerateDirectories().Where(x => LaneFolderMatch.IsMatch(x.Name)).ToArray();
            _laneDirs = _intensitiesDir.EnumerateDirectories().Where(x => LaneFolderMatch.IsMatch(x.Name)).ToArray();
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
                    },
                    Reads = new Reads { Read = _readStructure?.Reads }
                };
                return _run;
            }

            var serializer = new XmlSerializer(typeof(RunInfo));
            using var runFile = File.OpenRead(_runInfo!.FullName);
            var info = serializer.Deserialize(runFile) as RunInfo;

            _run = info?.Run ?? throw new Exception("Could not read " + _runInfo.FullName);
            if (_readStructure?.Reads != null)
            {
                _run.Reads.Read = _readStructure!.Reads;
            }
            return _run;
        }

        private static List<string> GetTileSet(DirectoryInfo dir)
        {
            return dir.GetDirectories("C*", SearchOption.TopDirectoryOnly)
                .SelectMany(d => d.EnumerateFiles().Select(x => x.Name.Split('.')[0].Split('_')[2]))
                .Distinct()
                .ToList();
        }

        public async IAsyncEnumerable<ClusterData> ReadClusterData([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            static bool Predicate(Read r) => r.IsIndexedRead.Equals("Y", StringComparison.OrdinalIgnoreCase);

            var runInfo = RunInfo();
            var readIndex = _readStructure != null ? _readStructure.Reads.FindIndex(Predicate) : runInfo.Reads.Read?.FindIndex(Predicate);
            if (!readIndex.HasValue)
            {
                throw new Exception("Read index could not be found");
            }
            var laneReaders = CreateLaneReaders(runInfo).ToList();
            foreach (var reader in laneReaders.AsParallel())
            {
                await foreach (var p in ReadBclData(reader, runInfo, readIndex.Value, cancellationToken).ConfigureAwait(false))
                {
                    yield return p;
                }

                await reader.Reader.DisposeAsync().ConfigureAwait(false);
            }
        }

        private IEnumerable<SampleReader> CreateLaneReaders(Run runInfo)
        {
            return from dir in _baseCallLaneDirs
                let lane = LaneFolderMatch.Match(dir.Name).Groups["lane"].Value.Trim('0')
                let laneNo = int.Parse(lane.AsSpan())
                let tiles =
                    runInfo.FlowcellLayout?.TileSet?.Tiles == null
                        ? GetTileSet(dir)
                        : (from t in runInfo.FlowcellLayout.TileSet.Tiles.Tile
                            where !t.Contains('_') || t.StartsWith(lane + '_')
                            select TileNumberMatch.Match(t).Groups["tile"].Value).ToList()
                let cycleDirs =
                    dir.GetDirectories().Length == 0
                        ? new[] { dir }
                        : dir.EnumerateDirectories("C*", SearchOption.TopDirectoryOnly)
                let files = GetReadFileInfos(cycleDirs, tiles).ToList()
                let tileFiles = files.Any(file => file.Name.Split('.')[0].Contains('_'))
                from tile in tileFiles ? tiles : tiles.Take(1)
                let tileNo = int.Parse(tile.AsSpan())
                orderby tileNo
                let filterFileName = tileFiles ? $"s_{lane}_{tile}.filter" : $"s_{lane}.filter"
                let filterFilePath = Path.Combine(dir.FullName, filterFileName)
                let filterReader =
                    File.Exists(filterFilePath)
                        ? new FilterFileReader(new FileInfo(filterFilePath))
                        : (IEnumerable<bool>)new PassThroughFilter()
                select new SampleReader(
                    laneNo,
                    laneNo,
                    tileNo,
                    new BclReader(files, runInfo.Reads.Read!, new BclQualityEvaluationStrategy(2), false),
                    GetPositionReader(laneNo, tileNo, lane),
                    filterReader);
        }

        private static IEnumerable<FileInfo> GetReadFileInfos(IEnumerable<DirectoryInfo> cycleDirs, List<string> tiles)
        {
            return from d in cycleDirs
                   orderby int.Parse(d.Name.Split('.')[0][1..])
                   from f in d.GetFiles("*.bgzf")
                       .Concat(d.GetFiles("*.gz"))
                       .Concat(d.GetFiles("*.bcl"))
                   let fileNameWithoutExtension = f.Name.Split('.')[0]
                   where tiles.Count == 0
                         || !fileNameWithoutExtension.Contains('_')
                         || tiles.Contains(fileNameWithoutExtension.Split('_')[2])
                   orderby f.Name
                   select f;
        }

        private static async IAsyncEnumerable<ClusterData> ReadBclData(
            SampleReader reader,
            Run runInfo,
            int readIndex,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var enumerator = reader.PositionReader.GetAsyncEnumerator(cancellationToken);
            await using var positionEnumerator = enumerator.ConfigureAwait(false);
            var enumerable = (IAsyncEnumerable<ReadData[]>)reader.Reader;
            using var filter = reader.Filter.GetEnumerator();
            await foreach (var data in enumerable.ConfigureAwait(false))
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    throw new Exception("Could not read position for sequence read");
                }

                if (!filter.MoveNext())
                {
                    throw new Exception("Could not read filter for cluster");
                }

                var filtered = filter.Current;
                var index = readIndex == -1
                    ? reader.Sample.ToString()
                    : Encoding.ASCII.GetString(data[readIndex].Bases);

                for (var i = 0; i < data.Length; i++)
                {
                    yield return new ClusterData(
                        index,
                        data[i].Bases,
                        Array.ConvertAll(data[i].Qualities, b => (byte)(b + 33)),
                        data[i].Type,
                        reader.Lane,
                        reader.Tile,
                        enumerator.Current,
                        data.Count(x => x.Type == ReadType.Barcode) > 1,
                        filtered);
                }
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
        public SampleReader(int lane, int sample, int tile, BclReader reader, IAsyncEnumerable<IPositionalData> positionReader, IEnumerable<bool> filter)
        {
            Lane = lane;
            Sample = sample;
            Tile = tile;
            Reader = reader;
            PositionReader = positionReader;
            Filter = filter;
        }

        public int Lane { get; }

        public int Sample { get; }

        public int Tile { get; }
        public BclReader Reader { get; }
        public IAsyncEnumerable<IPositionalData> PositionReader { get; }
        public IEnumerable<bool> Filter { get; }
    }
}
