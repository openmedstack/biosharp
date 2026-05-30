namespace OpenMedStack.BioSharp.Benchmarks;

using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Calculations.BurrowsWheeler;
using OpenMedStack.BioSharp.Model;

/// <summary>
/// Benchmarks comparing the FM-index and the hash-map <see cref="ReferenceIndex"/>
/// for:
///   1. Index construction time (both built in-process; JIT is warm by iteration 1).
///   2. Single-read candidate-window discovery (<c>FindCandidateWindows</c>).
///
/// This is the inner loop of the alignment pipeline — a read that maps to N
/// candidate windows will run N Smith-Waterman alignments.  Faster seeding
/// directly reduces total pipeline latency.
///
/// Apples-to-apples contract
/// ──────────────────────────
/// • Both seeders receive the same <c>Sequence reference</c> and the same
///   <c>Sequence read</c>.
/// • Neither seeder is rebuilt per iteration (only per <c>[GlobalSetup]</c>),
///   so the measurement is pure lookup throughput.
/// • The <c>Build</c> benchmarks rebuild the index every iteration so that
///   construction cost is isolated and reproducible.
/// </summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 5, iterationCount: 20)]
public class SeederComparisonBenchmarks
{
    private Sequence _reference1k   = null!;
    private Sequence _reference100k = null!;
    private Sequence _reference1M   = null!;
    private Sequence _read150bp     = null!;
    private Sequence _read75bp      = null!;

    private ReferenceIndex _index1k   = null!;
    private ReferenceIndex _index100k = null!;
    private FmIndexSeeder  _fmSeeder1k   = null!;
    private FmIndexSeeder  _fmSeeder100k = null!;

    private static Sequence MakeRef(string id, int length)
    {
        const string bases = "ACGT";
        var random = new Random(42 + length); // deterministic
        var buf = new char[length];
        for (var i = 0; i < length; i++)
        {
            buf[i] = bases[random.Next(4)];
        }

        return new Sequence(id, new string(buf).AsMemory(), new string('I', length).AsMemory());
    }

    private static Sequence MakeRead(string id, Sequence reference, int length, int offset, int numMutations = 1)
    {
        var refSpan = reference.GetData().Span;
        var buf = new char[length];
        for (var i = 0; i < length; i++)
        {
            buf[i] = refSpan[(offset + i) % refSpan.Length];
        }

        // Introduce a controlled substitution to exercise the seeder
        if (numMutations > 0 && length > numMutations)
        {
            var random = new Random(7);
            const string altBases = "ACGT";
            for (var m = 0; m < numMutations; m++)
            {
                var pos = random.Next(length);
                buf[pos] = altBases[(altBases.IndexOf(buf[pos]) + 1) % 4];
            }
        }

        return new Sequence(id, new string(buf).AsMemory(), new string('I', length).AsMemory());
    }

    [GlobalSetup]
    public void Setup()
    {
        _reference1k   = MakeRef("ref1k",   1_000);
        _reference100k = MakeRef("ref100k", 100_000);
        _reference1M   = MakeRef("ref1M",   1_000_000);

        // Place read in the middle of the reference so both seeders must find it
        _read150bp = MakeRead("r150", _reference100k, 150, 50_000);
        _read75bp  = MakeRead("r75",  _reference1k,    75,    500);

        var idxOptions = new ReferenceIndex.IndexOptions
        {
            SeedSize          = 11,
            WindowPadding     = 64,
            MaxCandidateWindowsPerRead = 8,
            MaxSeedHitsPerKmer = 64
        };
        _index1k   = new ReferenceIndex(_reference1k,   idxOptions);
        _index100k = new ReferenceIndex(_reference100k, idxOptions);

        _fmSeeder1k   = new FmIndexSeeder(_reference1k);
        _fmSeeder100k = new FmIndexSeeder(_reference100k);
    }

    // ── Construction benchmarks ───────────────────────────────────────────────

    /// <summary>Build a hash-map k-mer index for a 1 kb reference.</summary>
    [Benchmark(Baseline = true, Description = "HashMap-Build-1kb")]
    [BenchmarkCategory("Construction", "1kb")]
    public ReferenceIndex HashMap_Build_1kb()
    {
        var opts = new ReferenceIndex.IndexOptions { SeedSize = 11, WindowPadding = 64 };
        return new ReferenceIndex(_reference1k, opts);
    }

    /// <summary>Build an FM-index for a 1 kb reference.</summary>
    [Benchmark(Description = "FmIndex-Build-1kb")]
    [BenchmarkCategory("Construction", "1kb")]
    public FmIndexSeeder FmIndex_Build_1kb()
    {
        return new FmIndexSeeder(_reference1k);
    }

    /// <summary>Build a hash-map k-mer index for a 100 kb reference.</summary>
    [Benchmark(Description = "HashMap-Build-100kb")]
    [BenchmarkCategory("Construction", "100kb")]
    public ReferenceIndex HashMap_Build_100kb()
    {
        var opts = new ReferenceIndex.IndexOptions { SeedSize = 11, WindowPadding = 64 };
        return new ReferenceIndex(_reference100k, opts);
    }

    /// <summary>Build an FM-index for a 100 kb reference.</summary>
    [Benchmark(Description = "FmIndex-Build-100kb")]
    [BenchmarkCategory("Construction", "100kb")]
    public FmIndexSeeder FmIndex_Build_100kb()
    {
        return new FmIndexSeeder(_reference100k);
    }

    /// <summary>Build an FM-index for a 1 Mb reference.</summary>
    [Benchmark(Description = "FmIndex-Build-1Mb")]
    [BenchmarkCategory("Construction", "1Mb")]
    public FmIndexSeeder FmIndex_Build_1Mb()
    {
        return new FmIndexSeeder(_reference1M);
    }

    // ── Seeding benchmarks — pre-built index, measure lookup only ────────────

    /// <summary>HashMap seeder: find candidate windows for a 75 bp read on 1 kb reference.</summary>
    [Benchmark(Baseline = false, Description = "HashMap-Seed-75bp-1kb")]
    [BenchmarkCategory("Seeding", "75bp", "1kb")]
    public ReferenceIndex.CandidateWindow[] HashMap_Seed_75bp_1kb()
    {
        return _index1k.FindCandidateWindows(_read75bp);
    }

    /// <summary>FmIndex seeder: find candidate windows for a 75 bp read on 1 kb reference.</summary>
    [Benchmark(Description = "FmIndex-Seed-75bp-1kb")]
    [BenchmarkCategory("Seeding", "75bp", "1kb")]
    public ReferenceIndex.CandidateWindow[] FmIndex_Seed_75bp_1kb()
    {
        return _fmSeeder1k.FindCandidateWindows(_read75bp);
    }

    /// <summary>HashMap seeder: find candidate windows for a 150 bp read on 100 kb reference.</summary>
    [Benchmark(Description = "HashMap-Seed-150bp-100kb")]
    [BenchmarkCategory("Seeding", "150bp", "100kb")]
    public ReferenceIndex.CandidateWindow[] HashMap_Seed_150bp_100kb()
    {
        return _index100k.FindCandidateWindows(_read150bp);
    }

    /// <summary>FmIndex seeder: find candidate windows for a 150 bp read on 100 kb reference.</summary>
    [Benchmark(Description = "FmIndex-Seed-150bp-100kb")]
    [BenchmarkCategory("Seeding", "150bp", "100kb")]
    public ReferenceIndex.CandidateWindow[] FmIndex_Seed_150bp_100kb()
    {
        return _fmSeeder100k.FindCandidateWindows(_read150bp);
    }
}
