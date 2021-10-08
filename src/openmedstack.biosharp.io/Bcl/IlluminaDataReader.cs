namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
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
            else if (_run.Reads.Read?.All(x => x.Type == ReadType.S) == true)
            {
                // If read types aren't set, assume that the shortest reads are barcodes and the rest are templates.
                var first = true;
                foreach (var group in _run.Reads.Read.GroupBy(r => r.NumCycles).OrderBy(x => x.Key))
                {
                    foreach (var read in group)
                    {
                        read.Type = first ? ReadType.B : ReadType.T;
                    }

                    first = false;
                }
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

        public async IAsyncEnumerable<ClusterData> ReadClusterData(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var runInfo = RunInfo();
            var sampleReaders = CreateLaneReaders(runInfo).ToList();
            var source = sampleReaders
                .Select(r => r.ReadBclData(cancellationToken))
                .Interleave(cancellationToken);
            await foreach (var p in source.ConfigureAwait(false))
            {
                yield return p;
            }

            var tasks = Task.WhenAll(sampleReaders.Select(r => r.Reader.DisposeAsync().AsTask()));
            await tasks.ConfigureAwait(false);
            tasks.Dispose();
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

    internal static class LinqExtensions
    {
        public static async IAsyncEnumerable<T> Interleave<T>(this IEnumerable<IAsyncEnumerable<T>> enumerables, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var enumerators = enumerables.Select(e => e.GetAsyncEnumerator(cancellationToken)).ToList();
            while (enumerators.Count > 0)
            {
                var toCheck = enumerators.ToArray();
                foreach (var enumerator in toCheck)
                {
                    if (!await enumerator.MoveNextAsync(cancellationToken).ConfigureAwait(false))
                    {
                        enumerators.Remove(enumerator);
                        await enumerator.DisposeAsync().ConfigureAwait(false);
                    }
                }

                foreach (var enumerator in enumerators)
                {
                    yield return enumerator.Current;
                }
            }
        }
    }
}
