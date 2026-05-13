namespace OpenMedStack.BioSharp.Io.Bcl;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Model.Bcl;

public partial class IlluminaDataReader
{
    private const int LaneReaderPrefetchCount = 4;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<IlluminaDataReader> _logger;
    private readonly ReadStructure? _readStructure;

    private readonly FileInfo? _runInfo;

    //private readonly FileInfo? _sampleSheetInfo;
    private readonly DirectoryInfo[] _baseCallLaneDirs;
    private readonly DirectoryInfo[] _laneDirs;
    private Run? _run;

    public IlluminaDataReader(DirectoryInfo runDir, ILoggerFactory loggerFactory, ReadStructure? readStructure = null)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<IlluminaDataReader>();
        _readStructure = readStructure;
        _runInfo = runDir.EnumerateFiles("RunInfo.xml", SearchOption.TopDirectoryOnly).SingleOrDefault();
        var dataDir = runDir.EnumerateDirectories("Data", SearchOption.TopDirectoryOnly).Single();
        var intensitiesDir = dataDir.EnumerateDirectories("Intensities", SearchOption.TopDirectoryOnly).Single();
        var baseCallDir = intensitiesDir.EnumerateDirectories("BaseCalls", SearchOption.TopDirectoryOnly).Single();
        //_sampleSheetInfo = _baseCallDir.EnumerateFiles("SampleSheet.csv", SearchOption.TopDirectoryOnly).SingleOrDefault();
        _baseCallLaneDirs = baseCallDir.EnumerateDirectories().Where(x => LaneFolderRegex().IsMatch(x.Name)).ToArray();
        _laneDirs = intensitiesDir.EnumerateDirectories().Where(x => LaneFolderRegex().IsMatch(x.Name)).ToArray();
    }

    public Run RunInfo()
    {
        if (_run != null)
        {
            return _run;
        }

        if (_runInfo == null)
        {
            LogCreatingRunInfoBecauseNoRuninfoXmlFileWasProvided();
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

        LogDeserializingRuninfoXml();
        using var runFile = File.Open(
            _runInfo.FullName,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                Share = FileShare.Read
            });
        var document = XDocument.Load(runFile);
        _run = ParseRunInfo(document, _runInfo.FullName);
        if (_readStructure?.Reads != null)
        {
            LogSubstitutingReadStructuresWithManualOverridesStructure(_readStructure);
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

    private static Run ParseRunInfo(XDocument document, string path)
    {
        var root = document.Root ?? throw new Exception($"Could not read {path}");
        var runElement = GetRequiredElement(root, "Run", path);

        return new Run
        {
            Id = GetRequiredAttribute(runElement, "Id", path),
            Number = ParseRequiredIntAttribute(runElement, "Number", path),
            Flowcell = GetRequiredElementValue(runElement, "Flowcell", path),
            Instrument = GetRequiredElementValue(runElement, "Instrument", path),
            Date = GetRequiredElementValue(runElement, "Date", path),
            Reads = ParseReads(GetRequiredElement(runElement, "Reads", path), path),
            FlowcellLayout = ParseFlowcellLayout(GetRequiredElement(runElement, "FlowcellLayout", path), path),
            AlignToPhiX = ParseAlignToPhiX(runElement.Element("AlignToPhiX")),
            ImageDimensions = ParseImageDimensions(runElement.Element("ImageDimensions"), path),
            ImageChannels = ParseImageChannels(runElement.Element("ImageChannels"))
        };
    }

    private static Reads ParseReads(XElement element, string path)
    {
        return new Reads
        {
            Read = element.Elements("Read").Select(readElement => ParseRead(readElement, path)).ToList()
        };
    }

    private static Read ParseRead(XElement element, string path)
    {
        return new Read
        {
            Number = ParseRequiredIntAttribute(element, "Number", path),
            NumCycles = ParseRequiredIntAttribute(element, "NumCycles", path),
            IsIndexedRead = GetOptionalAttribute(element, "IsIndexedRead") ?? "N"
        };
    }

    private static FlowcellLayout ParseFlowcellLayout(XElement element, string path)
    {
        return new FlowcellLayout
        {
            LaneCount = ParseRequiredSByteAttribute(element, "LaneCount", path),
            SurfaceCount = ParseOptionalSByteAttribute(element, "SurfaceCount") ?? 0,
            SwathCount = ParseOptionalSByteAttribute(element, "SwathCount") ?? 0,
            TileCount = ParseOptionalIntAttribute(element, "TileCount") ?? 0,
            FlowcellSide = GetOptionalAttribute(element, "FlowcellSide"),
            TileSet = ParseTileSet(element.Element("TileSet"))
        };
    }

    private static TileSet? ParseTileSet(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        return new TileSet
        {
            TileNamingConvention = GetOptionalAttribute(element, "TileNamingConvention"),
            Tiles = ParseTiles(element.Element("Tiles"))
        };
    }

    private static Tiles? ParseTiles(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        return new Tiles
        {
            Tile = element.Elements("Tile").Select(tile => tile.Value).Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList()
        };
    }

    private static AlignToPhiX? ParseAlignToPhiX(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        return new AlignToPhiX
        {
            Lane = element.Element("Lane")?.Value
        };
    }

    private static ImageDimensions? ParseImageDimensions(XElement? element, string path)
    {
        if (element is null)
        {
            return null;
        }

        return new ImageDimensions
        {
            Width = ParseRequiredIntAttribute(element, "Width", path),
            Height = ParseRequiredIntAttribute(element, "Height", path)
        };
    }

    private static ImageChannels? ParseImageChannels(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        return new ImageChannels
        {
            Name = element.Elements("Name").Select(name => name.Value).Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList()
        };
    }

    private static XElement GetRequiredElement(XElement parent, string name, string path)
    {
        return parent.Element(name) ?? throw new Exception($"Could not read {path}: missing <{name}>");
    }

    private static string GetRequiredElementValue(XElement parent, string name, string path)
    {
        var value = parent.Element(name)?.Value;
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new Exception($"Could not read {path}: missing <{name}> value");
    }

    private static string GetRequiredAttribute(XElement element, string name, string path)
    {
        var value = GetOptionalAttribute(element, name);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new Exception($"Could not read {path}: missing {name} attribute");
    }

    private static string? GetOptionalAttribute(XElement element, string name)
    {
        return element.Attribute(name)?.Value;
    }

    private static int ParseRequiredIntAttribute(XElement element, string name, string path)
    {
        return int.TryParse(GetRequiredAttribute(element, name, path), out var value)
            ? value
            : throw new Exception($"Could not read {path}: invalid {name} attribute");
    }

    private static int? ParseOptionalIntAttribute(XElement element, string name)
    {
        var value = GetOptionalAttribute(element, name);
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static sbyte ParseRequiredSByteAttribute(XElement element, string name, string path)
    {
        return sbyte.TryParse(GetRequiredAttribute(element, name, path), out var value)
            ? value
            : throw new Exception($"Could not read {path}: invalid {name} attribute");
    }

    private static sbyte? ParseOptionalSByteAttribute(XElement element, string name)
    {
        var value = GetOptionalAttribute(element, name);
        return sbyte.TryParse(value, out var parsed) ? parsed : null;
    }

    private static List<string> GetTileSet(DirectoryInfo dir)
    {
        return dir.GetDirectories("C*", SearchOption.TopDirectoryOnly)
            .SelectMany(d => d.EnumerateFiles().Select(x => string.Join('_', x.Name.Split('.')[0].Split('_')[1..])))
            .Distinct()
            .ToList();
    }

    public int[] GetAllLanes()
    {
        return _laneDirs.Select(d => LaneFolderRegex().Match(d.Name).Groups["lane"].Value)
            .Select(int.Parse)
            .ToArray();
    }

    public async IAsyncEnumerable<SampleReader> ReadClusterData(
        int lane,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var runInfo = RunInfo();
        var pendingReaders = new Queue<Task<SampleReader>>(LaneReaderPrefetchCount);
        await using var readerTasks = CreateLaneReaders(lane, runInfo, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (pendingReaders.Count < LaneReaderPrefetchCount &&
               await readerTasks.MoveNextAsync().ConfigureAwait(false))
        {
            pendingReaders.Enqueue(readerTasks.Current);
        }

        while (pendingReaders.Count > 0)
        {
            while (pendingReaders.Count < LaneReaderPrefetchCount &&
                   await readerTasks.MoveNextAsync().ConfigureAwait(false))
            {
                pendingReaders.Enqueue(readerTasks.Current);
            }

            yield return await pendingReaders.Dequeue().ConfigureAwait(false);
        }
    }

    private async IAsyncEnumerable<Task<SampleReader>> CreateLaneReaders(
        int lane,
        Run runInfo,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var laneName = lane.ToString();
        var dir =
            _baseCallLaneDirs.Single(d => LaneFolderRegex().Match(d.Name).Groups["lane"].Value.Trim('0') == laneName);

        var tileIndexFileName = new FileInfo(Path.Combine(dir.FullName, $"s_{lane}.bci"));
        var tileNames = (from t in runInfo.FlowcellLayout.TileSet?.Tiles?.Tile ?? GetTileSet(dir)
                         let tileMatch = TileNumberRegex().Match(t)
                         where tileMatch.Success
                         where tileMatch.Groups["lane"].Value == laneName
                         select tileMatch.Groups["tile"].Value).ToList();
        IAsyncEnumerable<TileIndexRecord> tiles = File.Exists(tileIndexFileName.FullName)
            ? new TileIndex(tileIndexFileName)
            : new FileStuctureTileIndex(tileNames.Select(int.Parse));
        var files = GetLaneFileInfos(dir, tileNames);
        var indexReaders = files.Where(f => File.Exists($"{f.FullName}bci"))
            .ToDictionary(f => f.FullName, f => (IBclIndexReader)new BclIndexReader(f));
        await foreach (var tileRecord in tiles.ConfigureAwait(false).WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return CreateSampleReader(
                lane,
                runInfo,
                dir,
                tileRecord,
                laneName,
                files,
                indexReaders);
        }
    }

    private async Task<SampleReader> CreateSampleReader(
        int lane,
        Run runInfo,
        DirectoryInfo dir,
        TileIndexRecord tileRecord,
        string laneName,
        List<FileInfo> files,
        Dictionary<string, IBclIndexReader> indexReaders)
    {
        var filterReader = await GetFilterReader(lane, dir, tileRecord).ConfigureAwait(false);
        var positionReader = GetPositionReader(lane, tileRecord.Tile, laneName);
        _logger.LogInformation(
            "Created reader for lane: {Lane}, tile: {Tile} with {ClusterCount} on {Thread}",
            lane,
            tileRecord.Tile,
            tileRecord.NumClustersInTile == int.MaxValue
                ? "all clusters in file"
                : $"{tileRecord.NumClustersInTile} clusters",
            Environment.CurrentManagedThreadId);
        //await Task.Yield();
        var reader = await BclReader.Create(
            files,
            runInfo.Reads.Read!,
            tileRecord,
            new BclQualityEvaluationStrategy(2),
            _loggerFactory.CreateLogger<BclReader>(),
            indexReaders).ConfigureAwait(false);
        return new SampleReader(
            runInfo,
            lane,
            lane,
            reader,
            positionReader,
            filterReader);
    }

    private static async Task<IEnumerable<bool>> GetFilterReader(
        int lane,
        DirectoryInfo dir,
        TileIndexRecord tileRecord)
    {
        var filterFileName = File.Exists(Path.Combine(dir.FullName, $"s_{lane}_{tileRecord}.filter"))
            ? $"s_{lane}_{tileRecord}.filter"
            : $"s_{lane}.filter";
        var filterFilePath = Path.Combine(dir.FullName, filterFileName);
        IEnumerable<bool> filterReader = File.Exists(filterFilePath)
            ? await FilterFileReader.Create(new FileInfo(filterFilePath)).ConfigureAwait(false)
            : new PassThroughFilter();
        return filterReader;
    }

    private static List<FileInfo> GetLaneFileInfos(DirectoryInfo dir, List<string> tileNames)
    {
        var cycleDirs = dir.GetDirectories().Length == 0
            ? [dir]
            : dir.EnumerateDirectories("C*", SearchOption.TopDirectoryOnly);
        var files = GetReadFileInfos(cycleDirs, tileNames).ToList();
        return files;
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

    private ILocationReader GetPositionReader(int lane, int tile, string sample)
    {
        var files = _laneDirs.Single(d => d.Name[^1..].Equals(lane.ToString()))
            .GetFiles();
        var positionFile = files.SingleOrDefault(f => f.Name.StartsWith($"s_{sample}_{tile}"))
         ?? files.Single(f => f.Name.StartsWith($"s_{sample}"));
        return Path.GetExtension(positionFile.Name) switch
        {
            ".clocs" => new ClocsFileReader(positionFile),
            ".locs" => new LocsFileReader(positionFile),
            _ => throw new NotSupportedException(
                $"Unsupported file extension {Path.GetExtension(positionFile.Name)}")
        };
    }

    [GeneratedRegex("L(?<lane>\\d{3})", RegexOptions.Compiled)]
    private static partial Regex LaneFolderRegex();
    [GeneratedRegex(@"((?<lane>(\d+))_)?(?<tile>\d+)", RegexOptions.Compiled)]
    private static partial Regex TileNumberRegex();

    [LoggerMessage(LogLevel.Information, "Creating run info because no RunInfo.xml file was provided")]
    partial void LogCreatingRunInfoBecauseNoRuninfoXmlFileWasProvided();

    [LoggerMessage(LogLevel.Information, "Deserializing RunInfo.xml")]
    partial void LogDeserializingRuninfoXml();

    [LoggerMessage(LogLevel.Information, "Substituting read structures with manual overrides: {Structure}")]
    partial void LogSubstitutingReadStructuresWithManualOverridesStructure(ReadStructure structure);
}
