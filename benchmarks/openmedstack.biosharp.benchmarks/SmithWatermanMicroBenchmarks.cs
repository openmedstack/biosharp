using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.Benchmarks;

/// <summary>
/// Smith-Waterman alignment micro-benchmarks at different reference window sizes.
/// Isolates the DP cost from seeding so we can profile the inner loop directly.
/// </summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 5, iterationCount: 30)]
public class SmithWatermanMicroBenchmarks
{
    private Sequence _ref200bp  = null!;
    private Sequence _ref500bp  = null!;
    private Sequence _ref2kbp   = null!;
    private Sequence _read75bp  = null!;
    private Sequence _read150bp = null!;

    private static Sequence MakeSeq(string id, int length, Random rng)
    {
        const string bases = "ACGT";
        var buf = new char[length];
        for (var i = 0; i < length; i++)
        {
            buf[i] = bases[rng.Next(4)];
        }

        return new Sequence(id, new string(buf).AsMemory(), new string('I', length).AsMemory());
    }

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(17);
        _ref200bp  = MakeSeq("r200",   200, rng);
        _ref500bp  = MakeSeq("r500",   500, rng);
        _ref2kbp   = MakeSeq("r2k",  2_000, rng);
        _read75bp  = MakeSeq("q75",     75, rng);
        _read150bp = MakeSeq("q150",   150, rng);
    }

    [Benchmark(Baseline = true, Description = "SW-75bp-on-200bp-ref")]
    public AlignmentResult? Sw_75bp_200bp() => SmithWatermanAligner.Align(_ref200bp,  _read75bp,  minScore: 10);

    [Benchmark(Description = "SW-150bp-on-500bp-ref")]
    public AlignmentResult? Sw_150bp_500bp() => SmithWatermanAligner.Align(_ref500bp,  _read150bp, minScore: 10);

    [Benchmark(Description = "SW-150bp-on-2kb-ref")]
    public AlignmentResult? Sw_150bp_2kb()   => SmithWatermanAligner.Align(_ref2kbp,   _read150bp, minScore: 10);

    [Benchmark(Description = "SW-150bp-on-500bp-ref-banded")]
    public AlignmentResult? Sw_150bp_500bp_Banded() =>
        SmithWatermanAligner.Align(_ref500bp, _read150bp, bandWidth: 32, minScore: 10);

    [Benchmark(Description = "SW-150bp-on-2kb-ref-banded")]
    public AlignmentResult? Sw_150bp_2kb_Banded() =>
        SmithWatermanAligner.Align(_ref2kbp, _read150bp, bandWidth: 32, minScore: 10);
}
