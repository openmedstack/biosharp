namespace OpenMedStack.BioSharp.Calculations.Tests;

using System.Linq;
using Alignment;
using Model;
using Xunit;

public class ReferenceIndexTests
{
    [Fact]
    public void FindCandidateWindows_UniqueRead_ReturnsExpectedWindow()
    {
        var prefix = new string('A', 200);
        var target = "ACGTGATTACAGGTT";
        var suffix = new string('C', 200);
        var reference = new Sequence(
            "chr1",
            (prefix + target + suffix).ToCharArray(),
            new string('I', prefix.Length + target.Length + suffix.Length).ToCharArray());
        var read = new Sequence("read1", target.ToCharArray(), new string('I', target.Length).ToCharArray());

        var index = new ReferenceIndex(reference, new ReferenceIndex.IndexOptions
        {
            SeedSize = 6,
            WindowPadding = 12,
            MaxCandidateWindowsPerRead = 4,
            MaxSeedHitsPerKmer = 8
        });

        var windows = index.FindCandidateWindows(read);

        Assert.NotEmpty(windows);
        var best = windows.First();
        Assert.True(best.Start <= prefix.Length);
        Assert.True(best.End >= prefix.Length + target.Length);
    }

    [Fact]
    public void FindCandidateWindows_RepetitiveSeedFallsBackOnlyForSmallReference()
    {
        var reference = new Sequence("chr1", new string('A', 5000).ToCharArray(), new string('I', 5000).ToCharArray());
        var read = new Sequence("read1", new string('A', 30).ToCharArray(), new string('I', 30).ToCharArray());

        var index = new ReferenceIndex(reference, new ReferenceIndex.IndexOptions
        {
            SeedSize = 10,
            MaxSeedHitsPerKmer = 4,
            SmallReferenceFullScanThreshold = 1024
        });

        var windows = index.FindCandidateWindows(read);

        Assert.Empty(windows);
    }

    /// <summary>
    /// Ambiguous multi-candidate: the read sequence appears at two distinct loci in the
    /// reference.  The index must return ≥ 2 candidate windows so the aligner can score
    /// both and choose the best, rather than silently dropping one mapping.
    /// </summary>
    [Fact]
    public void FindCandidateWindows_AmbiguousRead_ReturnsTwoCandidates()
    {
        // Place the same 15-bp target at positions 100 and 600 in a 1000-bp reference.
        const string target = "ACGTGATTACAGGTT";
        var refChars = new char[1000];
        new string('T', 1000).CopyTo(0, refChars, 0, 1000);
        target.CopyTo(0, refChars, 100, target.Length);
        target.CopyTo(0, refChars, 600, target.Length);

        var reference = new Sequence("chr1", refChars, new string('I', 1000).ToCharArray());
        var read = new Sequence("read1", target.ToCharArray(), new string('I', target.Length).ToCharArray());

        var index = new ReferenceIndex(reference, new ReferenceIndex.IndexOptions
        {
            SeedSize = 6,
            WindowPadding = 20,
            MaxCandidateWindowsPerRead = 4,
            MaxSeedHitsPerKmer = 64   // allow repetitive seeds so both loci are seen
        });

        var windows = index.FindCandidateWindows(read);

        // Both copy locations must produce a candidate window.
        Assert.True(windows.Length >= 2, $"Expected ≥2 candidates for a duplicated target, got {windows.Length}");

        var coveredFirst  = windows.Any(w => w.Start <= 100 && w.End >= 100 + target.Length);
        var coveredSecond = windows.Any(w => w.Start <= 600 && w.End >= 600 + target.Length);
        Assert.True(coveredFirst,  "No candidate window covers the first copy at position 100");
        Assert.True(coveredSecond, "No candidate window covers the second copy at position 600");
    }

    /// <summary>
    /// Unmapped read: a read with no matching k-mers produces no candidate windows against
    /// a large reference (above the small-reference full-scan threshold).
    /// </summary>
    [Fact]
    public void FindCandidateWindows_UnmappedRead_ReturnsEmpty()
    {
        // Reference is all-A; read is all-T with no shared 6-mers.
        var reference = new Sequence("chr1", new string('A', 2000).ToCharArray(), new string('I', 2000).ToCharArray());
        var read = new Sequence("read1", new string('T', 30).ToCharArray(), new string('I', 30).ToCharArray());

        var index = new ReferenceIndex(reference, new ReferenceIndex.IndexOptions
        {
            SeedSize = 6,
            MaxSeedHitsPerKmer = 8,
            SmallReferenceFullScanThreshold = 1000  // 2000-bp ref is above threshold
        });

        var windows = index.FindCandidateWindows(read);

        Assert.Empty(windows);
    }
}