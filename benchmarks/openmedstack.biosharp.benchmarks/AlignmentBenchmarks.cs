namespace OpenMedStack.BioSharp.Benchmarks;

using BenchmarkDotNet.Attributes;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Model;

[MemoryDiagnoser]
public class AlignmentBenchmarks
{
    private Sequence _smallReference = null!;
    private Sequence _largeReference = null!;
    private Sequence _read = null!;
    private VariantCallingPipeline _largePipeline = null!;

    [GlobalSetup]
    public void Setup()
    {
        const string target = "ACGTGATTACAGGTTCCGATTA";
        _read = new Sequence("read", target.ToCharArray(), new string('I', target.Length).ToCharArray());

        var smallPrefix = new string('T', 256);
        var smallSuffix = new string('G', 256);
        var smallReferenceString = smallPrefix + target + smallSuffix;
        _smallReference = new Sequence(
            "smallRef",
            smallReferenceString.ToCharArray(),
            new string('I', smallReferenceString.Length).ToCharArray());

        var largePrefix = new string('A', 100_000);
        var largeSuffix = new string('C', 100_000);
        var largeReferenceString = largePrefix + target + largeSuffix;
        _largeReference = new Sequence(
            "largeRef",
            largeReferenceString.ToCharArray(),
            new string('I', largeReferenceString.Length).ToCharArray());

        _largePipeline = new VariantCallingPipeline(_largeReference, "largeRef", new VariantCallingPipeline.PipelineOptions
        {
            SeedSize = 6,
            CandidateWindowPadding = 32,
            MaxCandidateWindowsPerRead = 4,
            MaxSeedHitsPerKmer = 16,
            MinAlignmentScore = 10,
            EnableSoftClipRealignment = false,
            EnableGraphSvDetection = false
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
        return _largePipeline.ProcessReadAsync(_read).GetAwaiter().GetResult();
    }
}