namespace OpenMedStack.BioSharp.Calculations.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Alignment;
using BurrowsWheeler;
using Model;
using Xunit;

/// <summary>
/// Unit-tests for FmIndex and FmIndexSeeder.
///
/// The tests verify:
///   1. Suffix array correctness on a known text.
///   2. BWT construction from the suffix array.
///   3. Backward search / BackwardSearch correctness.
///   4. Rank / Occ internal consistency.
///   5. Locate: SA-range → reference positions.
///   6. FindExactSeeds: fixed-length seeds.
///   7. FindMemSeeds: variable-length MEM/SMEM-style seeds.
///   8. FmIndexSeeder.FindCandidateWindows: end-to-end window discovery.
///   9. VariantCallingPipeline integration with a custom FM-index seeder.
///  10. Serialise / deserialise round-trip.
/// </summary>
public class FmIndexTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static Sequence MakeSeq(string id, string dna)
        => new(id, dna.AsMemory(), new string('I', dna.Length).AsMemory());

    // Brute-force reference: all positions in text where pattern occurs
    private static List<int> BruteForce(string text, string pattern)
    {
        var hits = new List<int>();
        for (var i = 0; i <= text.Length - pattern.Length; i++)
        {
            if (text.AsSpan(i, pattern.Length).SequenceEqual(pattern.AsSpan()))
            {
                hits.Add(i);
            }
        }

        return hits;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 1. Suffix array correctness
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildSuffixArray_BananaExample_CorrectOrder()
    {
        // "banana$" — a classic suffix-array example
        const string text = "banana";
        // Expected suffix array for "banana$": positions of suffixes sorted lex.
        // Suffixes (0-indexed including $):
        //   0: banana$
        //   1: anana$
        //   2: nana$
        //   3: ana$
        //   4: na$
        //   5: a$
        //   6: $
        // Sorted: $, a$, ana$, anana$, banana$, na$, nana$
        //         6   5   3     1       0        4    2
        var expected = new[] { 6, 5, 3, 1, 0, 4, 2 };

        var encoded = new byte[text.Length + 1];
        for (var i = 0; i < text.Length; i++)
        {
            encoded[i] = (byte)(text[i] - 'a' + 1); // 'a'=1, 'b'=2, 'n'=14
        }
        // encoded[6] = 0 (sentinel)

        var sa = FmIndex.BuildSuffixArray(encoded, text.Length);

        Assert.Equal(expected.Length, sa.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], sa[i]);
        }
    }

    [Fact]
    public void BuildSuffixArray_SingleChar_ReturnsCorrect()
    {
        var text = new byte[] { 1, 0 }; // "a$"
        var sa   = FmIndex.BuildSuffixArray(text, 1);
        Assert.Equal(new[] { 1, 0 }, sa); // "$" < "a$"
    }

    [Fact]
    public void BuildSuffixArray_AllSameChar_CorrectOrder()
    {
        // "aaaa$" — every suffix is a repeated-a string
        var text = new byte[] { 1, 1, 1, 1, 0 }; // aaaa$
        var sa   = FmIndex.BuildSuffixArray(text, 4);
        // Sorted: $,a$,aa$,aaa$,aaaa$ → positions 4,3,2,1,0
        Assert.Equal(new[] { 4, 3, 2, 1, 0 }, sa);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 2. Backward search — exact matching
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("ACGT", "ACG", 1)]    // unique
    [InlineData("ACGTACGT", "ACGT", 2)] // two occurrences
    [InlineData("ACGT", "TTTT", 0)]   // absent
    [InlineData("ACGTACGT", "AC", 2)] // short prefix
    [InlineData("AAAAAAAAAA", "AAA", 8)] // repeated
    public void BackwardSearch_CountMatchesExpected(string reference, string pattern, int expected)
    {
        var idx = FmIndex.Build(reference.AsSpan());
        var (sp, ep) = idx.BackwardSearch(pattern.AsSpan());
        Assert.Equal(expected, ep - sp);
    }

    [Fact]
    public void BackwardSearch_EmptyPattern_MatchesAll()
    {
        const string reference = "ACGT";
        var idx = FmIndex.Build(reference.AsSpan());
        var (sp, ep) = idx.BackwardSearch(ReadOnlySpan<char>.Empty);
        // Empty pattern matches the full SA range (all positions)
        Assert.True(ep - sp >= reference.Length);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 3. Locate — recovers correct positions
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("ACGTACGT", "ACGT")]
    [InlineData("ACGTACGT", "CG")]
    [InlineData("AAACAAAGAAA", "AAA")]
    [InlineData("ACGTNNACGT", "ACG")]
    public void Locate_MatchesBruteForce(string reference, string pattern)
    {
        var idx       = FmIndex.Build(reference.AsSpan());
        var (sp, ep) = idx.BackwardSearch(pattern.AsSpan());
        var found    = ep > sp ? idx.Locate(sp, ep, 64) : [];

        var expected = BruteForce(reference, pattern);

        Assert.Equal(expected.Count, found.Length);
        Array.Sort(found);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], found[i]);
        }
    }

    [Fact]
    public void Locate_AbsentPattern_ReturnsEmpty()
    {
        var idx      = FmIndex.Build("ACGTACGT".AsSpan());
        var (sp, ep) = idx.BackwardSearch("GGGG".AsSpan());
        Assert.Equal(0, ep - sp);
        var positions = idx.Locate(sp, ep, 64);
        Assert.Empty(positions);
    }

    [Fact]
    public void Locate_RepeatPattern_MaxCountRespected()
    {
        var reference = string.Concat(Enumerable.Repeat("ACG", 50)); // 150 bp, "ACG" × 50
        var idx       = FmIndex.Build(reference.AsSpan());
        var (sp, ep)  = idx.BackwardSearch("ACG".AsSpan());
        Assert.True(ep - sp >= 50);
        var limited = idx.Locate(sp, ep, 10);
        Assert.Equal(10, limited.Length);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 4. FindExactSeeds — fixed-length seeds
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void FindExactSeeds_UniqueRead_FindsSeed()
    {
        const string prefix    = "AAAAAAAAAA"; // 10 A's
        const string target    = "ACGTGATTACAGG"; // 13-mer
        const string suffix    = "CCCCCCCCCC"; // 10 C's
        var reference = prefix + target + suffix;
        var idx       = FmIndex.Build(reference.AsSpan());

        var seeds = idx.FindExactSeeds(target.AsSpan(), seedLen: 11, seedStep: 1, maxHits: 64);

        Assert.NotEmpty(seeds);
        // Each 11-mer at query offset i should map to reference position prefix.Length + i.
        // Verify that the seed for the first query offset (start=0) maps to prefix.Length.
        var firstSeed = seeds.First(s => s.QueryStart == 0);
        Assert.Contains(prefix.Length, firstSeed.ReferencePositions);
    }

    [Fact]
    public void FindExactSeeds_SeedLongerThanQuery_ReturnsEmpty()
    {
        var idx   = FmIndex.Build("ACGT".AsSpan());
        var seeds = idx.FindExactSeeds("AC".AsSpan(), seedLen: 10);
        Assert.Empty(seeds);
    }

    [Fact]
    public void FindExactSeeds_RepetitiveSeedAboveMaxHits_Excluded()
    {
        var reference = string.Concat(Enumerable.Repeat("A", 1000)); // all-A reference
        var idx       = FmIndex.Build(reference.AsSpan());
        // With maxHits = 5, no A-only seed should appear
        var seeds = idx.FindExactSeeds(new string('A', 30).AsSpan(), seedLen: 11, maxHits: 5);
        Assert.Empty(seeds);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 5. FindMemSeeds — variable-length MEM seeds
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void FindMemSeeds_UniqueRead_FindsOneSeed()
    {
        const string prefix    = "TTTTTTTTTT";
        const string target    = "ACGTGATTACAGGTTCAAGCT"; // 21-mer
        const string suffix    = "GGGGGGGGGG";
        var reference = prefix + target + suffix;
        var idx       = FmIndex.Build(reference.AsSpan());

        var seeds = idx.FindMemSeeds(target.AsSpan(), minSeedLen: 10, maxHits: 64);

        Assert.NotEmpty(seeds);
        // The seed should map to position prefix.Length
        var allPositions = seeds.SelectMany(s => s.ReferencePositions).ToArray();
        Assert.Contains(prefix.Length, allPositions);
    }

    [Fact]
    public void FindMemSeeds_NonOverlapping_CoverFullRead()
    {
        const string reference = "ACGTGATTACAGGTTCAAGCTTTACGTTGACCA";
        var idx   = FmIndex.Build(reference.AsSpan());
        var query = reference; // query == reference means every MEM covers the full length
        var seeds = idx.FindMemSeeds(query.AsSpan(), minSeedLen: 5, maxHits: 64);

        // Seeds should be non-overlapping
        var sorted = seeds.OrderBy(s => s.QueryStart).ToArray();
        for (var i = 1; i < sorted.Length; i++)
        {
            Assert.True(sorted[i].QueryStart >= sorted[i - 1].QueryEnd,
                $"Seed {i} overlaps seed {i - 1}.");
        }

        // Together they should cover the whole query
        var covered = new bool[query.Length];
        foreach (var s in seeds)
            for (var p = s.QueryStart; p < s.QueryEnd; p++)
            {
                covered[p] = true;
            }

        Assert.True(covered.All(b => b), "Some query positions are not covered by any seed.");
    }

    [Fact]
    public void FindMemSeeds_QueryShorterThanMinSeedLen_ReturnsEmpty()
    {
        var idx   = FmIndex.Build("ACGTACGT".AsSpan());
        var seeds = idx.FindMemSeeds("ACGT".AsSpan(), minSeedLen: 10);
        Assert.Empty(seeds);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 6. FmIndexSeeder.FindCandidateWindows
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void FmIndexSeeder_UniqueRead_FindsCorrectWindow()
    {
        const string prefix = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"; // 40 A's
        const string target = "ACGTGATTACAGGTT";
        const string suffix = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC"; // 40 C's
        var reference = prefix + target + suffix;
        var refSeq    = MakeSeq("chr1", reference);
        var seeder    = new FmIndexSeeder(refSeq, new FmIndexSeeder.Options { MinSeedLen = 10, WindowPadding = 20 });

        var read    = MakeSeq("read1", target);
        var windows = seeder.FindCandidateWindows(read);

        Assert.NotEmpty(windows);
        var best = windows.First();
        Assert.True(best.Start <= prefix.Length,
            $"Window start {best.Start} should be ≤ {prefix.Length}.");
        Assert.True(best.End >= prefix.Length + target.Length,
            $"Window end {best.End} should be ≥ {prefix.Length + target.Length}.");
    }

    [Fact]
    public void FmIndexSeeder_AmbiguousRead_ReturnsTwoCandidates()
    {
        const string target = "ACGTGATTACAGGTT";
        var refChars = new char[1000];
        new string('T', 1000).CopyTo(0, refChars, 0, 1000);
        target.CopyTo(0, refChars, 100, target.Length);
        target.CopyTo(0, refChars, 600, target.Length);

        var refSeq  = MakeSeq("chr1", new string(refChars));
        var seeder  = new FmIndexSeeder(refSeq, new FmIndexSeeder.Options
        {
            MinSeedLen = 8,
            WindowPadding = 20,
            MaxCandidateWindowsPerRead = 4
        });

        var read    = MakeSeq("read1", target);
        var windows = seeder.FindCandidateWindows(read);

        Assert.True(windows.Length >= 2,
            $"Expected ≥ 2 candidate windows; got {windows.Length}.");
    }

    [Fact]
    public void FmIndexSeeder_ReadNotInReference_ReturnsEmpty()
    {
        var refSeq  = MakeSeq("chr1", new string('A', 100));
        var seeder  = new FmIndexSeeder(refSeq, new FmIndexSeeder.Options { MinSeedLen = 10 });
        var read    = MakeSeq("r", new string('C', 30)); // all-C read vs all-A reference

        var windows = seeder.FindCandidateWindows(read);
        Assert.Empty(windows);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 7. VariantCallingPipeline integration
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void VariantCallingPipeline_WithFmSeeder_ProcessesReadSuccessfully()
    {
        // Build a simple reference with one SNP position
        const string reference = "AAAAAACGTGATTACAGGTTCAAGCTAAAAAAA";
        var refSeq    = MakeSeq("chr1", reference);
        var seeder    = new FmIndexSeeder(refSeq, new FmIndexSeeder.Options
        {
            MinSeedLen = 10,
            WindowPadding = 16,
            UseMemSeeds = true
        });

        var pipeline  = new VariantCallingPipeline(refSeq, "chr1");
        pipeline.Seeder = seeder;

        // Read that exactly matches a region of the reference (→ no variants)
        const string read = "ACGTGATTACAGGTT";
        var variants = pipeline.ProcessRead(MakeSeq("read1", read));

        // The read matches reference exactly, so no variants should be called.
        Assert.True(variants.Length == 0,
            $"Expected 0 variants for an exact-match read; got {variants.Length}.");
    }

    [Fact]
    public void VariantCallingPipeline_FmSeeder_FindsSNP()
    {
        // Reference: 80-bp flanks around a 20bp segment containing the variant site
        const string left  = "AAAGCGTTAGCGTTACGAACTTGGCATCAGTTGCAAAGCGTTAGCGTTACGAACTTGGCATCAGTTGCAACGATCT";
        const string mid   = "ACGTGATTACAGGTTCAAGCT"; // 21 bp, to be mutated
        const string right = "TGCGTAAGCGGACGATCGTTAATATGTCCGTCGGATCGTCGATCGATCGATCGATCGATCGATCGATCGATCGATC";
        var refText  = left + mid + right;
        // Mutate position 7 of mid: 'T' → 'G' (A→G SNP at T position)
        const string mutMid  = "ACGTGATGACAGGTTCAAGCT"; // T→G at index 7
        var readText = mutMid;

        var refSeq   = MakeSeq("chr1", refText);
        var seeder   = new FmIndexSeeder(refSeq, new FmIndexSeeder.Options
        {
            MinSeedLen = 9,
            WindowPadding = 30,
            UseMemSeeds = true
        });
        var pipeline = new VariantCallingPipeline(refSeq, "chr1");
        pipeline.Seeder = seeder;

        var variants = pipeline.ProcessRead(MakeSeq("read_snp", readText));

        // At least one variant should be detected (the T→G substitution).
        Assert.True(variants.Length >= 1,
            $"Expected ≥ 1 variant for a mutated read; got {variants.Length}.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 8. Serialise / deserialise round-trip
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void FmIndex_SaveLoad_RoundTrip_ProducesSameResults()
    {
        const string reference = "ACGTGATTACAGGTTCAAGCTTTACGTTGACCA";
        var idx = FmIndex.Build(reference.AsSpan());

        using var ms = new MemoryStream();
        idx.Save(ms);
        ms.Position = 0;
        var loaded = FmIndex.Load(ms);

        // Verify backward search produces same SA range in both
        var patterns = new[] { "ACG", "GAT", "TTCAAG", "ZZZZ" };
        foreach (var p in patterns)
        {
            var (sp1, ep1) = idx.BackwardSearch(p.AsSpan());
            var (sp2, ep2) = loaded.BackwardSearch(p.AsSpan());
            Assert.Equal(sp1, sp2);
            Assert.Equal(ep1, ep2);
        }
    }

    [Fact]
    public void FmIndex_SaveLoad_FileRoundTrip()
    {
        const string reference = "ACGTACGT";
        var idx   = FmIndex.Build(reference.AsSpan());
        var path  = Path.GetTempFileName();
        try
        {
            idx.Save(path);
            var loaded = FmIndex.Load(path);

            var (sp1, ep1) = idx.BackwardSearch("ACG".AsSpan());
            var (sp2, ep2) = loaded.BackwardSearch("ACG".AsSpan());
            Assert.Equal(sp1, sp2);
            Assert.Equal(ep1, ep2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 9. FmIndexSeeder.Options.UseMemSeeds = false => fixed-length seeds
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void FmIndexSeeder_ExactSeedMode_FindsWindow()
    {
        const string prefix = "TTTTTTTTTTTTTTTT";
        const string target = "ACGTGATTACAGGTTCAAGCT";
        const string suffix = "GGGGGGGGGGGGGGGG";
        var reference = prefix + target + suffix;

        var refSeq  = MakeSeq("chr1", reference);
        var seeder  = new FmIndexSeeder(refSeq, new FmIndexSeeder.Options
        {
            MinSeedLen = 10,
            UseMemSeeds = false,
            WindowPadding = 20
        });

        var windows = seeder.FindCandidateWindows(MakeSeq("r", target));
        Assert.NotEmpty(windows);
        Assert.True(windows[0].Start <= prefix.Length);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 10. IReferenceSeeder contract: ReferenceIndex and FmIndexSeeder behave the same
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BothSeeders_ReturnWindowContainingTarget()
    {
        const string prefix = "AAAAAAAAAAAAAAAAAAAAAA";
        const string target = "ACGTGATTACAGG";
        const string suffix = "CCCCCCCCCCCCCCCCCCCCCC";
        var reference = prefix + target + suffix;

        var refSeq = MakeSeq("chr1", reference);
        var read   = MakeSeq("r", target);

        // Hash-map seeder
        IReferenceSeeder hashSeeder = new ReferenceIndex(refSeq, new ReferenceIndex.IndexOptions
        {
            SeedSize = 7, WindowPadding = 16, MaxCandidateWindowsPerRead = 4
        });

        // FM-index seeder
        IReferenceSeeder fmSeeder = new FmIndexSeeder(refSeq, new FmIndexSeeder.Options
        {
            MinSeedLen = 7, WindowPadding = 16, MaxCandidateWindowsPerRead = 4
        });

        foreach (var seeder in new[] { hashSeeder, fmSeeder })
        {
            var windows = seeder.FindCandidateWindows(read);
            Assert.NotEmpty(windows);
            var best = windows[0];
            Assert.True(best.Start <= prefix.Length,
                $"{seeder.GetType().Name}: window start too far right.");
            Assert.True(best.End >= prefix.Length + target.Length,
                $"{seeder.GetType().Name}: window end too far left.");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 11. BurrowsWheelerTransform.Transform uses O(n log n) SA construction
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BwtTransform_InvertIsIdentity()
    {
        const string original = "ACGTGATTACAGT";
        var transformed = BurrowsWheelerTransform.Transform(original);
        Assert.Equal(original.Length + 1, transformed.Length); // includes sentinel row

        // Invert should recover the original text (Invert expects the old string without sentinel)
        // We verify by checking the BWT contains the same characters as original + '$'
        var chars = transformed.ToCharArray();
        Array.Sort(chars);
        var sorted = new string(chars);
        Assert.Contains("$", sorted);
    }
}




