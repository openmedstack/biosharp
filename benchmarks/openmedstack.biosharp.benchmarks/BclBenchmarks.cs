namespace OpenMedStack.BioSharp.Benchmarks;

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Io.Bcl;
using OpenMedStack.BioSharp.Io.FastQ;
using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Bcl;

[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
[ShortRunJob]
public class BclBenchmarks
{
    private string _tempDirectory = null!;
    private FileInfo[] _cycleFiles = null!;
    private TileIndexRecord _tile = null!;
    private Read[] _reads = null!;

    [Params(25, 75)] public int CycleCount { get; set; }

    [Params(2048, 8192)] public int ClusterCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"biosharp-bcl-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        _reads =
        [
            new Read
            {
                Number = 1,
                NumCycles = CycleCount,
                IsIndexedRead = "N",
                Type = ReadType.T
            }
        ];
        _tile = new TileIndexRecord(Tile: 1101, NumClustersInTile: ClusterCount, IndexOfFirstClusterInTile: 0, ZeroBasedTileNumber: 0);
        _cycleFiles = Enumerable.Range(1, CycleCount)
            .Select(CreateCycleFile)
            .ToArray();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Benchmark]
    public async Task<int> DecodeTile()
    {
        await using var reader = await CreateReader().ConfigureAwait(false);
        var clusterCount = 0;
        await foreach (var _ in reader)
        {
            clusterCount++;
        }

        return clusterCount;
    }

    [Benchmark]
    public async Task<int> DecodeAndWriteFastQTile()
    {
        await using var reader = await CreateReader().ConfigureAwait(false);
        await using var output = new MemoryStream();
        await using var indexOutput = new MemoryStream();
        await using var writer = new FastQWriter(
            NullLogger.Instance,
            output,
            indexOutput,
            CompressionLevel.Fastest,
            leaveOpen: true);

        var sequenceCount = 0;
        await foreach (var readData in reader)
        {
            await writer.Write(CreateSequence(readData), default).ConfigureAwait(false);
            sequenceCount++;
        }

        return sequenceCount + (int)output.Length;
    }

    private Task<BclReader> CreateReader()
    {
        return BclReader.Create(
            _cycleFiles,
            _reads,
            _tile,
            new BclQualityEvaluationStrategy(2),
            NullLogger<BclReader>.Instance);
    }

    private FileInfo CreateCycleFile(int cycle)
    {
        var filePath = Path.Combine(_tempDirectory, $"{cycle:D4}.bcl");
        var payload = new byte[4 + ClusterCount];
        BitConverter.TryWriteBytes(payload.AsSpan(0, 4), ClusterCount);
        for (var index = 0; index < ClusterCount; index++)
        {
            var quality = 30 + ((index + cycle) % 8);
            var baseCode = (index + cycle) & 0x03;
            payload[4 + index] = (byte)((quality << 2) | baseCode);
        }

        File.WriteAllBytes(filePath, payload);
        return new FileInfo(filePath);
    }

    private static Sequence CreateSequence(ReadData[] readData)
    {
        var read = readData[0];
        var qualities = new char[read.Qualities.Length];
        var sourceQualities = read.Qualities.Span;
        for (var index = 0; index < sourceQualities.Length; index++)
        {
            qualities[index] = (char)(sourceQualities[index] + 33);
        }

        return new Sequence(
            $"cluster-{read.ReadIndex}",
            read.Bases.ToArray(),
            qualities);
    }
}
