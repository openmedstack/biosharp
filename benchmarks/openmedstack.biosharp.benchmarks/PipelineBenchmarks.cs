namespace OpenMedStack.BioSharp.Benchmarks;

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Model;

[MemoryDiagnoser]
public class PipelineBenchmarks
{
    private string _plainFastqPath = null!;
    private string _gzipFastqPath = null!;
    private Sequence _reference = null!;
    private Sequence _softClippedRead = null!;
    private AlignmentResult _softClippedAlignment = null!;
    private ReadRealigner _realigner = null!;

    [GlobalSetup]
    public void Setup()
    {
        const string target = "ACGTGATTACAGGTTCCGATTA";
        var referenceString = new string('A', 1024) + target + new string('C', 1024);
        _reference = new Sequence(
            "chrBench",
            referenceString.AsMemory(),
            new string('I', referenceString.Length).AsMemory());

        _plainFastqPath = Path.Combine(Path.GetTempPath(), $"biosharp-bench-{Guid.NewGuid():N}.fastq");
        _gzipFastqPath = _plainFastqPath + ".gz";

        var builder = new StringBuilder();
        for (var index = 0; index < 256; index++)
        {
            builder.Append('@').Append("read").Append(index).Append('\n');
            builder.Append(target).Append('\n');
            builder.Append('+').Append('\n');
            builder.Append('I', target.Length).Append('\n');
        }

        File.WriteAllText(_plainFastqPath, builder.ToString());
        using var input = File.OpenRead(_plainFastqPath);
        using var output = File.Create(_gzipFastqPath);
        using var gzip = new GZipStream(output, CompressionLevel.SmallestSize);
        input.CopyTo(gzip);

        var clippedRead = new string('T', 24) + target;
        _softClippedRead = new Sequence(
            "softclip",
            clippedRead.AsMemory(),
            new string('I', clippedRead.Length).AsMemory());
        _softClippedAlignment =
            new AlignmentResult(target, target, new string('|', target.Length), 42, 1024, 24, 0, false);
        _realigner = new ReadRealigner();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (File.Exists(_plainFastqPath)) File.Delete(_plainFastqPath);
        if (File.Exists(_gzipFastqPath)) File.Delete(_gzipFastqPath);
    }

    [Benchmark]
    public async Task<bool> StreamPlainFastQSerial()
    {
        var pipeline = CreatePipeline(1);
        return await pipeline.LoadFastQ(_plainFastqPath).ConfigureAwait(false);
    }

    [Benchmark]
    public async Task<bool> StreamGzipFastQParallel()
    {
        var pipeline = CreatePipeline(Environment.ProcessorCount > 1 ? 2 : 1);
        return await pipeline.LoadFastQ(_gzipFastqPath).ConfigureAwait(false);
    }

    [Benchmark]
    public ReadRealigner.RealignmentResult[] RealignSoftClippedRead()
    {
        return _realigner.Realign(_softClippedAlignment, _reference, "chrBench", _softClippedRead);
    }

    private VariantCallingPipeline CreatePipeline(int degreeOfParallelism)
    {
        return new VariantCallingPipeline(_reference, "chrBench", new VariantCallingPipeline.PipelineOptions
        {
            SeedSize = 6,
            CandidateWindowPadding = 32,
            MaxCandidateWindowsPerRead = 4,
            MaxSeedHitsPerKmer = 16,
            MinAlignmentScore = 10,
            EnableSoftClipRealignment = false,
            EnableGraphSvDetection = false,
            DegreeOfParallelism = degreeOfParallelism
        });
    }
}
