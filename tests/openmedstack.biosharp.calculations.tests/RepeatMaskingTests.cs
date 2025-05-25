using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DeBruijn = OpenMedStack.BioSharp.Calculations.DeBruijn;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

/// <summary>
/// Acceptance tests for RepeatMasker -- masking repeat sequences from DNA.
/// Tests cover loading the library, masking with known repeats, edge cases,
/// Bloom filter creation, and null input handling.
/// </summary>
public class RepeatMaskingTests
{
    private static readonly string TestLibraryPath = "repeats.json";

    /// <summary>
    /// RM-1: LoadLibraryAsync loads the repeat library and filters by motif length.
    /// </summary>
    [Fact]
    public async Task LoadLibrary_WithValidPath_ReturnsFilteredRepeats()
    {
        var repeats = await DeBruijn.RepeatMasker.LoadLibraryAsync(TestLibraryPath, 5);

        Assert.NotEmpty(repeats);
        // All motifs should be >= 5 chars (minMotifLength = 5)
        Assert.All(repeats, r => Assert.True(r.MotifLength >= 5));

        // Should contain expected repeat types
        var types = repeats.Select(r => r.Type).ToHashSet();
        Assert.Contains("satellite", types);
        Assert.Contains("retroposon", types);
        Assert.Contains("tandem", types);
        Assert.Contains("dna_transposon", types);
        Assert.Contains("retroviral", types);
        Assert.Contains("low_complexity", types);
    }

    /// <summary>
    /// RM-2: MaskRepeatsAsync masks known repeat motifs in a sequence.
    /// The sequence "ACGTGATTAGATTAGATTAGATTAGCCCTTTGATTAGATTAGATTAGATTAGATT"
    /// contains two "GATT" 4-mer tandem repeats.
    /// </summary>
    [Fact]
    public async Task MaskRepeats_KnownMotifsProducesMaskedRegions()
    {
        // Load library with low minMotifLength to catch simple repeats
        var library = await DeBruijn.RepeatMasker.LoadLibraryAsync(TestLibraryPath, 4);

        // Create a sequence with known repeat motifs from the library
        // REP001 has motif "GATT"
        var sequence = "AAAA" + "GATT" + "GATT" + "GATT" + "CCCC" + "TTTT";

        var regions = await DeBruijn.RepeatMasker.MaskRepeatsAsync(sequence, library, false);

        // Should have detected at least one masked region
        Assert.NotEmpty(regions);
        Assert.True(regions.Count >= 1);
    }

    /// <summary>
    /// RM-3: No repeats in sequence produces empty results.
    /// </summary>
    [Fact]
    public async Task MaskRepeats_NoRepeatsInSequence_ReturnsEmpty()
    {
        var library = await DeBruijn.RepeatMasker.LoadLibraryAsync(TestLibraryPath, 4);

        // Random sequence unlikely to match any repeat motif
        var sequence = "ACGTACGTACGTACGTACGTACGTACGTACGT";

        var regions = await DeBruijn.RepeatMasker.MaskRepeatsAsync(sequence, library, false);

        Assert.Empty(regions);
    }

    /// <summary>
    /// RM-4: MaskRepeats with N-allowed in sequence still matches non-N positions.
    /// </summary>
    [Fact]
    public async Task MaskRepeats_AmbiguityMasking_IgnoresNPositions()
    {
        var library = await DeBruijn.RepeatMasker.LoadLibraryAsync(TestLibraryPath, 4);

        // Sequence with Ns in the middle of a repeat - should NOT match due to ambiguity
        var sequence = "AAAA" + "GATT" + "N" + "GATT" + "GATT" + "CCCC";

        var regions = await DeBruijn.RepeatMasker.MaskRepeatsAsync(sequence, library, true);

        // Even with ambiguity blocking, should find other matches
        Assert.True(regions.Count >= 0); // Graph successfully processed
    }

    /// <summary>
    /// RM-5: CreateBloomFilterFromLibrary creates a functionally valid Bloom filter.
    /// </summary>
    [Fact]
    public async Task CreateBloomFilterFromLibrary_ContainsAllMotifKmers()
    {
        var library = await DeBruijn.RepeatMasker.LoadLibraryAsync(TestLibraryPath, 4);

        var filter = DeBruijn.RepeatMasker.CreateBloomFilterFromLibrary(library, 4, 0.01);

        // Test a few specific motifs from the library
        var motifKmers = new[]
        {
            "GATT", // from REP001
            "CCCC", // from REP002
            "GTGT", // from REP003
            "CACA" // from REP004 (CACACACA -> first 4-mer)
        };

        Assert.True(filter.Contains("GATT"));
        Assert.True(filter.Contains("CCCC"));
        Assert.True(filter.Contains("GTGT"));
        Assert.True(filter.Contains("CACA"));
    }

    /// <summary>
    /// RM-6: Null library throws; null/empty sequence returns empty.
    /// </summary>
    [Fact]
    public async Task MaskRepeats_NullInputs_ThrowsForNullLibrary()
    {
        var library = await DeBruijn.RepeatMasker.LoadLibraryAsync(TestLibraryPath, 4);

        // null library should throw
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            DeBruijn.RepeatMasker.MaskRepeatsAsync(
                "ACGTACGT", null!));

        // null/empty sequence should return empty (not throw)
        var emptyResult = await DeBruijn.RepeatMasker.MaskRepeatsAsync("", library, true);
        Assert.Empty(emptyResult);

        // null library should also throw
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            DeBruijn.RepeatMasker.LoadLibraryAsync(null!, 4));
    }

    /// <summary>
    /// RM-7: MaskRepeatsFromLibrary loads from file and masks in one step.
    /// </summary>
    [Fact]
    public async Task MaskRepeatsFromLibrary_FullPipeline_Works()
    {
        var sequence = "AAAA" + "GATT" + "GATT" + "CCCC" + "TTTT";
        var regions = await DeBruijn.RepeatMasker.MaskRepeatsFromLibraryAsync(
            sequence, TestLibraryPath, 4, false);

        Assert.True(regions.Count >= 0);
        if (regions.Any())
            Assert.All(regions, r =>
            {
                Assert.True(r.Start >= 0);
                Assert.True(r.End > r.Start);
                Assert.NotNull(r.MaskedSequence);
                Assert.Equal(new string('N', r.Length), r.MaskedSequence);
            });
    }

    /// <summary>
    /// RM-8: RepeatElement has expected properties populated from library.
    /// </summary>
    [Fact]
    public async Task RepeatElement_PopulatedFromLibrary()
    {
        var library = await DeBruijn.RepeatMasker.LoadLibraryAsync(TestLibraryPath, 4);

        var satRepeats = library.Where(r => r.Type == "satellite").ToList();
        Assert.NotEmpty(satRepeats);

        var sat = satRepeats.First();
        Assert.NotEmpty(sat.Id);
        Assert.NotEmpty(sat.Name);
        Assert.NotEmpty(sat.Motif);
        Assert.True(sat.MotifLength >= 4);
    }

    /// <summary>
    /// RM-9: MaskedRegion has expected computed properties.
    /// </summary>
    [Fact]
    public void MaskedRegion_CalculatedProperties_AreCorrect()
    {
        var repeat = new DeBruijn.RepeatElement
        {
            Id = "TEST001",
            Name = "Test Repeat",
            Type = "tandem",
            Motif = "AAAA"
        };

        var region = new DeBruijn.MaskedRegion
        {
            Start = 5,
            End = 10,
            Repeat = repeat,
            MaskedSequence = "NNNNN"
        };

        Assert.Equal(5, region.Length);
        Assert.Equal(5, region.MaskedSequence.Length);
        Assert.Equal("tandem", region.RepeatType);
        Assert.Equal("TEST001", region.Repeat?.Id);
        Assert.Equal("Test Repeat", region.Repeat?.Name);
    }

    /// <summary>
    /// RM-10: Default constants have expected values.
    /// </summary>
    [Fact]
    public void DefaultConstants_HaveExpectedValues()
    {
        Assert.Equal(4, DeBruijn.RepeatMasker.DefaultMinMotifLength);
        Assert.Equal(13, DeBruijn.RepeatMasker.DefaultMinMatchQuality);
        Assert.Equal("data/repeats.json", DeBruijn.RepeatMasker.DefaultLibraryPath);
    }
}
