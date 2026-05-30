using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using OpenMedStack.BioSharp.Calculations.BurrowsWheeler;
using OpenMedStack.BioSharp.Model;

namespace OpenMedStack.BioSharp.Benchmarks;

/// <summary>
/// Micro-benchmarks for the raw FM-index operations:
/// backward search, exact-seed finding, and MEM-seed finding.
/// These expose the algorithmic cost independent of the pipeline.
/// </summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 5, iterationCount: 20)]
public class FmIndexOperationBenchmarks
{
    private FmIndex _index100k = null!;
    private Sequence _read150bp = null!;
    private string _pattern19bp = null!;

    [GlobalSetup]
    public void Setup()
    {
        const string bases = "ACGT";
        var random = new Random(42);
        var refBuf = new char[100_000];
        for (var i = 0; i < refBuf.Length; i++)
        {
            refBuf[i] = bases[random.Next(4)];
        }

        var refStr = new string(refBuf);

        // Embed a known 19-mer at position 50000 for BackwardSearch
        const string knownMer = "ACGTGATTACAGGTTCCGA";
        refBuf[50_000] = 'A'; refBuf[50_001] = 'C'; refBuf[50_002] = 'G'; refBuf[50_003] = 'T';
        refBuf[50_004] = 'G'; refBuf[50_005] = 'A'; refBuf[50_006] = 'T'; refBuf[50_007] = 'T';
        refBuf[50_008] = 'A'; refBuf[50_009] = 'C'; refBuf[50_010] = 'A'; refBuf[50_011] = 'G';
        refBuf[50_012] = 'G'; refBuf[50_013] = 'T'; refBuf[50_014] = 'T'; refBuf[50_015] = 'C';
        refBuf[50_016] = 'C'; refBuf[50_017] = 'G'; refBuf[50_018] = 'A';
        refStr = new string(refBuf);

        _index100k  = FmIndex.Build(refStr, sampleRate: 32);
        _pattern19bp = knownMer;

        // 150 bp read starting at 50 000
        var readBuf = new char[150];
        refStr.CopyTo(50_000, readBuf, 0, 150);
        _read150bp = new Sequence("r150", new string(readBuf).AsMemory(), new string('I', 150).AsMemory());
    }

    /// <summary>Single backward search for a 19-mer.</summary>
    [Benchmark(Baseline = true, Description = "BackwardSearch-19mer")]
    public (int Sp, int Ep) BackwardSearch_19mer()
    {
        return _index100k.BackwardSearch(_pattern19bp);
    }

    /// <summary>Fixed-length exact seeds for a 150 bp read.</summary>
    [Benchmark(Description = "FindExactSeeds-150bp")]
    public IReadOnlyList<FmSeed> FindExactSeeds_150bp()
    {
        return _index100k.FindExactSeeds(_read150bp.GetData().Span, seedLen: 19, seedStep: 1);
    }

    /// <summary>Variable-length MEM seeds for a 150 bp read (BWA-MEM style).</summary>
    [Benchmark(Description = "FindMemSeeds-150bp")]
    public IReadOnlyList<FmSeed> FindMemSeeds_150bp()
    {
        return _index100k.FindMemSeeds(_read150bp.GetData().Span, minSeedLen: 19);
    }

    /// <summary>Locate: resolve SA positions for first 4 hits of a 19-mer.</summary>
    [Benchmark(Description = "Locate-4hits")]
    public int[] Locate_4hits()
    {
        var (sp, ep) = _index100k.BackwardSearch(_pattern19bp);
        return _index100k.Locate(sp, ep, maxCount: 4);
    }
}
