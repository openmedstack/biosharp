using System;
using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.Benchmarks;

using BenchmarkDotNet.Attributes;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Model;

[MarkdownExporterAttribute.GitHub]
[MemoryDiagnoser]
public class AlignmentBenchmarks
{
    private Sequence _smallReference = null!;
    private Sequence _largeReference = null!;
    private Sequence _read = null!;
    private VariantCallingPipeline _largePipeline = null!;

    [Params(false, true)] public bool EnableGraphSv;

    [Params(false, true)] public bool EnableSoftClipRealignment;

    [Params(5, 15, 25, 50)] public int KmerSize;

    [Params(5, 15, 25, 50)] public int MinGraphCoverage;

    [Params(10)] public int MaxCores;

    [GlobalSetup]
    public void Setup()
    {
        const string target = "ACGTGATTACAGGTTCCGATTA";
        _read = new Sequence("read", target.AsMemory(), new string('I', target.Length).AsMemory());

        var smallPrefix = new string('T', 256);
        var smallSuffix = new string('G', 256);
        var smallReferenceString = smallPrefix + target + smallSuffix;
        _smallReference = new Sequence(
            "smallRef",
            smallReferenceString.AsMemory(),
            new string('I', smallReferenceString.Length).AsMemory());

        var largePrefix = new string('A', 100_000);
        var largeSuffix = new string('C', 100_000);
        var largeReferenceString = largePrefix + target + largeSuffix;
        _largeReference = new Sequence(
            "largeRef",
            largeReferenceString.AsMemory(),
            new string('I', largeReferenceString.Length).AsMemory());

        _largePipeline = new VariantCallingPipeline(_largeReference, "largeRef", new VariantCallingPipeline.PipelineOptions
        {
            SeedSize = 6,
            KmerSize = KmerSize,
            MinGraphCoverage = MinGraphCoverage,
            CandidateWindowPadding = 32,
            MaxCandidateWindowsPerRead = 4,
            MaxSeedHitsPerKmer = 16,
            MinAlignmentScore = 10,
            EnableSoftClipRealignment = EnableSoftClipRealignment,
            EnableGraphSvDetection = EnableGraphSv,
            DegreeOfParallelism = MaxCores,
            CandidateAlignmentDegreeOfParallelism = MaxCores,
            ParallelCandidateWindowThreshold = 1,
        });
    }

    [Benchmark]
    public AlignmentResult? AlignShortReadAgainst1KbReference()
    {
        return SmithWatermanAligner.Align(_smallReference, _read, minScore: 10);
    }

    [Benchmark]
    public LocalVariantResult[] ProcessReadAgainstIndexedLargeReference()
    {
        return _largePipeline.ProcessRead(_read);
    }
}
