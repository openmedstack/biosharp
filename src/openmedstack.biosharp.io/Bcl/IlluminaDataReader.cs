namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
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
            }
            if (_run != null)
            {
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

        public async IAsyncEnumerable<Sequence> Read([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var runInfo = RunInfo();
            var laneReaders = (from dir in _baseCallLaneDirs
                               let lane = dir.Name[^1..]
                               let tiles = runInfo.FlowcellLayout?.TileSet?.Tiles == null ? GetTileSet(dir) :
                                   (from t in runInfo.FlowcellLayout.TileSet.Tiles.Tile
                                    where t.StartsWith(lane + '_')
                                    select t.Split('_')[1]).ToList()
                               from tile in tiles
                               let files = from d in dir.GetDirectories()
                                           orderby int.Parse(d.Name.Split('.')[0][1..])
                                           from f in d.GetFiles()
                                           where tiles.Contains(f.Name.Split('.')[0].Split('_')[2])
                                           select f
                               select new SampleReader(
                                   int.Parse(lane),
                                   int.Parse(lane),
                                   int.Parse(tile),
                                   new BclReader(
                                       files.ToList(),
                                       runInfo.Reads.Read.OrderBy(x => x.Number).Select(r => r.NumCycles).ToArray(),
                                       new BclQualityEvaluationStrategy(2),
                                       false))).ToList();
            foreach (var reader in laneReaders.AsParallel())
            {
                await foreach (var data in ((IAsyncEnumerable<BclData>)reader.Reader).ConfigureAwait(false))
                {
                    for (var i = 0; i < data.Bases.Length; i++)
                    {
                        var id = $"{runInfo.Instrument}:{runInfo.Number}:{runInfo.Flowcell}:{reader.Lane}:{reader.Tile}:0:0 {reader.Sample}:{i}:N";
                        yield return new Sequence(
                            id,
                            data.Bases[i],
                            data.Qualities[i].Select(b => (byte)(b + 33)).ToArray());
                    }
                }

                await reader.Reader.DisposeAsync().ConfigureAwait(false);
            }

        }
    }

    public class SampleReader
    {
        public SampleReader(int lane, int sample, int tile, BclReader reader)
        {
            Lane = lane;
            Sample = sample;
            Tile = tile;
            Reader = reader;
        }

        public int Lane { get; }

        public int Sample { get; }

        public int Tile { get; }
        public BclReader Reader { get; }
    }

    [XmlRoot(ElementName = "Read")]
    public class Read
    {
        [XmlAttribute(AttributeName = "Number")]
        public int Number { get; set; }

        [XmlAttribute(AttributeName = "NumCycles")]
        public int NumCycles { get; set; }

        [XmlAttribute(AttributeName = "IsIndexedRead")]
        public string IsIndexedRead { get; set; } = "";
    }

    [XmlRoot(ElementName = "Reads")]
    public class Reads
    {
        [XmlElement(ElementName = "Read")] public List<Read> Read { get; set; } = null!;
    }

    [XmlRoot(ElementName = "Tiles")]
    public class Tiles
    {
        [XmlElement(ElementName = "Tile")] public List<string> Tile { get; set; } = null!;
    }

    [XmlRoot(ElementName = "TileSet")]
    public class TileSet
    {
        [XmlElement(ElementName = "Tiles")] public Tiles Tiles { get; set; } = null!;

        [XmlAttribute(AttributeName = "TileNamingConvention")]
        public string? TileNamingConvention { get; set; }
    }

    [XmlRoot(ElementName = "FlowcellLayout")]
    public class FlowcellLayout
    {
        [XmlElement(ElementName = "TileSet")] public TileSet TileSet { get; set; } = null!;

        [XmlAttribute(AttributeName = "LaneCount")]
        public sbyte LaneCount { get; set; }

        [XmlAttribute(AttributeName = "SurfaceCount")]
        public sbyte SurfaceCount { get; set; }

        [XmlAttribute(AttributeName = "SwathCount")]
        public sbyte SwathCount { get; set; }

        [XmlAttribute(AttributeName = "TileCount")]
        public int TileCount { get; set; }

        [XmlAttribute(AttributeName = "FlowcellSide")]
        public string? FlowcellSide { get; set; }
    }

    [XmlRoot(ElementName = "ImageDimensions")]
    public class ImageDimensions
    {
        [XmlAttribute(AttributeName = "Width")]
        public int Width { get; set; }

        [XmlAttribute(AttributeName = "Height")]
        public int Height { get; set; }
    }

    [XmlRoot(ElementName = "ImageChannels")]
    public class ImageChannels
    {
        [XmlElement(ElementName = "Name")] public List<string> Name { get; set; } = null!;
    }

    [XmlRoot(ElementName = "Run")]
    public class Run
    {
        [XmlElement(ElementName = "Flowcell")] public string Flowcell { get; set; } = null!;

        [XmlElement(ElementName = "Instrument")]
        public string Instrument { get; set; } = null!;

        [XmlElement(ElementName = "Date")] public string Date { get; set; } = null!;

        [XmlElement(ElementName = "Reads")] public Reads Reads { get; set; } = null!;

        [XmlElement(ElementName = "FlowcellLayout")]
        public FlowcellLayout FlowcellLayout { get; set; } = null!;

        [XmlElement(ElementName = "AlignToPhiX")]
        public AlignToPhiX? AlignToPhiX { get; set; }

        [XmlElement(ElementName = "ImageDimensions")]
        public ImageDimensions? ImageDimensions { get; set; }

        [XmlElement(ElementName = "ImageChannels")]
        public ImageChannels? ImageChannels { get; set; }

        [XmlAttribute(AttributeName = "Id")] public string Id { get; set; } = null!;

        [XmlAttribute(AttributeName = "Number")]
        public int Number { get; set; }
    }

    [XmlRoot(ElementName = "AlignToPhiX")]
    public class AlignToPhiX
    {
        [XmlElement(ElementName = "Lane")]
        public string? Lane { get; set; }
    }

    [XmlRoot(ElementName = "RunInfo")]
    public class RunInfo
    {
        [XmlElement(ElementName = "Run")] public Run Run { get; set; } = null!;

        [XmlAttribute(AttributeName = "Version")]
        public int Version { get; set; }
    }
}
