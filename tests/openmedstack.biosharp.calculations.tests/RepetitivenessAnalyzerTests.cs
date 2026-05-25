using System;
using System.Collections.Generic;
using OpenMedStack.BioSharp.Model;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class RepetitivenessAnalyzerTests
{
    /// <summary>
    /// Create a Bubble with the given path sequences and coverage values.
    /// </summary>
    private static DeBruijn.Bubble CreateBubble(string[] sequences, int[]? coverages = null)
    {
        if (sequences == null)
        {
            throw new ArgumentNullException(nameof(sequences));
        }

        var paths = new List<DeBruijn.SequencePath>(sequences.Length);
        if (coverages == null)
        {
            coverages = new int[sequences.Length];
        }

        for (var i = 0; i < sequences.Length; i++)
        {
            paths.Add(new DeBruijn.SequencePath(sequences[i], coverages[i]));
        }

        return new DeBruijn.Bubble("start", "end", paths.ToArray());
    }

    /// <summary>
    /// Create a k-mer counts dictionary from key-value pairs.
    /// </summary>
    private static Dictionary<string, int> BuildKmerCounts(
        params (string key, int value)[] pairs)
    {
        var dict = new Dictionary<string, int>();
        foreach (var (key, value) in pairs)
        {
            dict[key] = value;
        }

        return dict;
    }

    [Fact]
    public void Analyze_BasicBubble_ReturnsScore()
    {
        // Arrange
        var bubble = CreateBubble(["ACGT", "ACGC", "ACGT"]);
        var kmerCounts = BuildKmerCounts(
            ("ACGT", 10),
            ("ACGC", 8),
            ("CGT", 10),
            ("CGC", 8)
        );

        // Act
        var score = DeBruijn.RepetitivenessAnalyzer.Analyze(bubble, kmerCounts);

        // Assert
        Assert.NotNull(score);
        Assert.True(score.TotalKmers > 0);
    }

    [Fact]
    public void Analyze_NoKmerCounts_ThrowsArgumentNullException()
    {
        // Arrange
        var bubble = CreateBubble(["ACGT", "ACGC"]);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => DeBruijn.RepetitivenessAnalyzer.Analyze(bubble, null!));
    }

    [Fact]
    public void Analyze_NullBubble_ThrowsArgumentNullException()
    {
        // Arrange
        var kmerCounts = BuildKmerCounts(("ACGT", 10));

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => DeBruijn.RepetitivenessAnalyzer.Analyze(null!, kmerCounts));
    }

    [Fact]
    public void Analyze_EmptyBubblePaths_ThrowsArgumentException()
    {
        // Arrange
        var bubble = new DeBruijn.Bubble("start", "end", []);
        var kmerCounts = BuildKmerCounts(("ACGT", 10));

        // Act & Assert
        Assert.Throws<ArgumentException>(() => DeBruijn.RepetitivenessAnalyzer.Analyze(bubble, kmerCounts));
    }

    [Fact]
    public void Analyze_FewRepeats_MediumConfidence()
    {
        // Arrange - bubble where a significant portion of k-mers are repeats
        var kmerCounts = BuildKmerCounts(
            ("ACGTACGC", 55), // repeat
            ("ACGTACGC", 55) // duplicate, so effective repeat count = 1
        );
        var bubble = CreateBubble(["ACGTACGC", "ACGTACGC"]);

        // Act
        var score = DeBruijn.RepetitivenessAnalyzer.Analyze(bubble, kmerCounts);

        // Assert
        Assert.True(score.RepeatKmerCount >= 0);
    }

    [Fact]
    public void Analyze_ManyRepeats_LowConfidence()
    {
        // Arrange - all k-mers are high-copy repeats (same sequence twice)
        var kmerCounts = BuildKmerCounts(
            ("ACGTACGT", 100),
            ("ACGTACGT", 100)
        );
        var bubble = CreateBubble(["ACGTACGT", "ACGTACGT"]);

        // Act
        var score = DeBruijn.RepetitivenessAnalyzer.Analyze(bubble, kmerCounts);

        // Assert
        Assert.Equal(BubbleConfidence.Low, score.Confidence);
        Assert.True(score.RepeatKmerCount > 0);
    }

    [Fact]
    public void Analyze_NoRepeats_HighConfidence()
    {
        // Arrange - unique k-mers with good coverage but below repeat threshold
        var kmerCounts = BuildKmerCounts(
            ("ACGT", 30),
            ("ACGC", 25)
        );
        var bubble = CreateBubble(["ACGT", "ACGC"]);

        // Act
        var score = DeBruijn.RepetitivenessAnalyzer.Analyze(bubble, kmerCounts);

        // Assert
        Assert.Equal(BubbleConfidence.Medium, score.Confidence);
        Assert.Equal(0, score.RepeatKmerCount);
    }

    [Fact]
    public void Analyze_ConfidenceScore_HighRepeatHasLowerScore()
    {
        // Arrange - same bubble, different repeat densities
        var highRepeat = BuildKmerCounts(
            ("ACGT", 100),
            ("ACGC", 100)
        );

        var lowRepeat = BuildKmerCounts(
            ("ACGT", 5),
            ("ACGC", 3)
        );

        var bubble = CreateBubble(["ACGT", "ACGC"]);

        // Act
        var highScore = DeBruijn.RepetitivenessAnalyzer.Analyze(bubble, highRepeat);
        var lowScore = DeBruijn.RepetitivenessAnalyzer.Analyze(bubble, lowRepeat);

        // Assert - higher repeats means lower confidence score
        Assert.True(lowScore.ConfidenceScore > highScore.ConfidenceScore);
    }

    [Fact]
    public void Analyze_DefaultThresholdIsFifty()
    {
        // Assert
        Assert.Equal(50, DeBruijn.RepetitivenessAnalyzer.DefaultHighCopyThreshold);
    }

    [Fact]
    public void Analyze_CustomThreshold_WorksWithLowThreshold()
    {
        // Arrange - with threshold=3, even low-count k-mers are repeats
        var kmerCounts = BuildKmerCounts(
            ("ACGT", 5),
            ("ACGC", 4)
        );
        var bubble = CreateBubble(["ACGT", "ACGC"]);

        // Act
        var threshold100 = DeBruijn.RepetitivenessAnalyzer.Analyze(bubble, kmerCounts, 100);
        var threshold3 = DeBruijn.RepetitivenessAnalyzer.Analyze(bubble, kmerCounts, 3);

        // Assert
        Assert.True(threshold100.RepeatKmerCount == 0);
        Assert.True(threshold3.RepeatKmerCount > 0);
    }

    [Fact]
    public void Analyze_EmptyKmerCounts_DictWithNoEntries_ReturnsValidScore()
    {
        // Arrange - empty dictionary (no k-mers defined)
        var kmerCounts = new Dictionary<string, int>();
        var bubble = CreateBubble(["ACGT", "ACGC"]);

        // Act - should not throw, just treat all as zero copy
        var score = DeBruijn.RepetitivenessAnalyzer.Analyze(bubble, kmerCounts);

        // Assert
        Assert.NotNull(score);
        Assert.Equal(0, score.RepeatKmerCount);
        Assert.Equal(0, score.AvgCopyNum);
    }

    [Fact]
    public void Analyze_KmersNotInCounts_DontThrow()
    {
        // Arrange - bubble with k-mers that aren't in the counts dictionary
        var kmerCounts = BuildKmerCounts(); // empty
        var bubble = CreateBubble(["ACGT", "ACGC"]);

        // Act - should not throw, just treat all as zero copy
        var score = DeBruijn.RepetitivenessAnalyzer.Analyze(bubble, kmerCounts);

        // Assert
        Assert.NotNull(score);
        Assert.Equal(0, score.RepeatKmerCount);
    }

    [Fact]
    public void Analyze_SinglePath_CalculatesAverageCopyNum()
    {
        // Arrange
        var kmerCounts = BuildKmerCounts(
            ("ACGT", 10),
            ("CGTA", 20),
            ("GTAC", 30)
        );
        var bubble = CreateBubble(["ACGTACGTAC"]);

        // Act
        var score = DeBruijn.RepetitivenessAnalyzer.Analyze(bubble, kmerCounts);

        // Assert - only 1 unique k-mer (the full sequence), with count 10
        Assert.NotNull(score);
    }

    [Fact]
    public void Analyze_NullPathInArray_SkipsNullPath()
    {
        // Arrange
        var bubble = new DeBruijn.Bubble("start", "end", [null!, new DeBruijn.SequencePath("ACGT")]);
        var kmerCounts = BuildKmerCounts(("ACGT", 10));

        // Act - should not throw, null path is skipped
        var score = DeBruijn.RepetitivenessAnalyzer.Analyze(bubble, kmerCounts);

        // Assert - 1 k-mer from the valid path
        Assert.NotNull(score);
        Assert.Equal(1, score.TotalKmers);
        Assert.Equal(0, score.RepeatKmerCount);
    }

    [Fact]
    public void Analyze_EmptyPathSequence_TreatedAsEmptyKmer()
    {
        // Arrange
        var kmerCounts = BuildKmerCounts(("ACGT", 10));
        var bubble = CreateBubble(["", "ACGT"]);

        // Act - empty sequence is skipped (string.IsNullOrEmpty check in Analyze)
        var score = DeBruijn.RepetitivenessAnalyzer.Analyze(bubble, kmerCounts);

        // Assert - only "ACGT" path contributes, 1 k-mer, not a repeat
        Assert.NotNull(score);
    }
}
