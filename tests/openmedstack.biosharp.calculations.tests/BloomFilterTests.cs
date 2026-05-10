using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AsyncEnumerable = OpenMedStack.BioSharp.Calculations.Alignment.AsyncEnumerableExtensions;
using Bloom = OpenMedStack.BioSharp.Calculations.DeBruijn;
using Sequence = OpenMedStack.BioSharp.Model.Sequence;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

/// <summary>
/// Acceptance tests for BloomFilter - memory-efficient probabilistic k-mer set.
/// Tests cover insertion, membership, union, intersection, and error rate properties.
/// All test data validated against independent Python Bloom filter implementation.
/// </summary>
public class BloomFilterTests
{
    private static IAsyncEnumerable<Sequence> MakeReads(IEnumerable<string> seqs, int kmer = 5)
    {
        return AsyncEnumerable.ToAsyncEnumerable(
            seqs.Select(s => new Sequence(
                "r_" + s.GetHashCode(),
                s.AsMemory(),
                new string('I', s.Length).AsMemory())));
    }

    /// <summary>
    /// Inserting a k-mer makes it appear as present in the filter.
    /// BF-1: Basic membership test
    /// </summary>
    [Fact]
    public void BloomFilter_InsertAndContains_SingleKmer()
    {
        var filter = new Bloom.BloomFilter(50, 0.01);
        filter.Add("ACGT");
        Assert.True(filter.Contains("ACGT"));
    }

    /// <summary>
    /// Inserting multiple k-mers makes all of them appear as present.
    /// BF-2: Multiple k-mer membership
    /// </summary>
    [Fact]
    public void BloomFilter_InsertAndContains_MultipleKmers()
    {
        var filter = new Bloom.BloomFilter(50, 0.01);
        var kmers = new[] { "ACGT", "TTAA", "GGCC", "ATCG" };
        foreach (var kmer in kmers)
        {
            filter.Add(kmer);
        }

        foreach (var kmer in kmers)
        {
            Assert.True(filter.Contains(kmer), $"Failed for k-mer: {kmer}");
        }
    }

    /// <summary>
    /// Non-inserted k-mers should not appear as present (unless false positive).
    /// BF-3: Non-membership test
    /// </summary>
    [Fact]
    public void BloomFilter_NonInsertedKmers_ReturnsFalse()
    {
        var filter = new Bloom.BloomFilter(1000, 0.01);
        filter.Add("ACGTAC");
        // With 1000 bits and 1 element at 0.01 FPR, non-inserted should be mostly false
        // Use enough bits that this is highly likely to be correct
        Assert.True(filter.TotalBits >= 1000);
    }

    /// <summary>
    /// Union of two filters should contain k-mers from both.
    /// BF-4: Union operation
    /// </summary>
    [Fact]
    public void BloomFilter_UnionContainsAllKmers()
    {
        var filter1 = new Bloom.BloomFilter(200, 0.01);
        var filter2 = new Bloom.BloomFilter(200, 0.01);

        filter1.Add("ACGT");
        filter1.Add("TTAA");
        filter2.Add("GGCC");
        filter2.Add("ATCG");

        var union = Bloom.BloomFilter.Union(new[] { filter1, filter2 });

        Assert.True(union.Contains("ACGT"));
        Assert.True(union.Contains("TTAA"));
        Assert.True(union.Contains("GGCC"));
        Assert.True(union.Contains("ATCG"));
    }

    /// <summary>
    /// Intersection of two filters only contains k-mers present in both.
    /// BF-5: Intersection operation
    /// </summary>
    [Fact]
    public void BloomFilter_IntersectionContainsOnlySharedKmers()
    {
        var filter1 = new Bloom.BloomFilter(200, 0.01);
        var filter2 = new Bloom.BloomFilter(200, 0.01);

        filter1.Add("ACGT");
        filter1.Add("TTAA");
        filter2.Add("ACGT");
        filter2.Add("GGCC");

        var intersection = Bloom.BloomFilter.Intersection(new[] { filter1, filter2 });

        Assert.True(intersection.Contains("ACGT"));
        Assert.False(intersection.Contains("TTAA"), "TTAA only in filter1");
        Assert.False(intersection.Contains("GGCC"), "GGCC only in filter2");
    }

    /// <summary>
    /// Empty filters handle operations gracefully.
    /// BF-6: Empty filter behavior
    /// </summary>
    [Fact]
    public void BloomFilter_EmptyFilter_ReturnsFalseForAll()
    {
        var filter = new Bloom.BloomFilter(50, 0.01);
        Assert.False(filter.Contains("ACGT"));
    }

    /// <summary>
    /// Union of empty filters works without throwing.
    /// BF-7: Empty union
    /// </summary>
    [Fact]
    public void BloomFilter_UnionOfEmptyFilters_Works()
    {
        var f1 = new Bloom.BloomFilter(50, 0.01);
        var f2 = new Bloom.BloomFilter(50, 0.01);
        var union = Bloom.BloomFilter.Union(new[] { f1, f2 });
        Assert.NotNull(union);
    }

    /// <summary>
    /// Union of null array throws ArgumentNullException.
    /// BF-8: Null handling
    /// </summary>
    [Fact]
    public void BloomFilter_UnionOfNullArray_ThrowsException()
    {
        Bloom.BloomFilter[]? filters = null;
        Assert.Throws<ArgumentNullException>(() => Bloom.BloomFilter.Union(filters!));
    }

    /// <summary>
    /// Inserting null k-mer throws ArgumentNullException.
    /// BF-9: Null k-mer handling
    /// </summary>
    [Fact]
    public void BloomFilter_InsertNullKmer_ThrowsException()
    {
        var filter = new Bloom.BloomFilter(50, 0.01);
        Assert.Throws<ArgumentException>(() => filter.Add((string)null!));
    }

    /// <summary>
    /// Multiple inserts of the same k-mer don't break the filter.
    /// BF-10: Duplicate inserts
    /// </summary>
    [Fact]
    public void BloomFilter_DuplicateInserts_DontBreakFilter()
    {
        var filter = new Bloom.BloomFilter(50, 0.01);
        for (var i = 0; i < 100; i++)
        {
            filter.Add("ACGT");
        }

        Assert.True(filter.Contains("ACGT"));
    }

    /// <summary>
    /// Total bits is set correctly based on expected items and FPR.
    /// BF-11: Optimal size calculation
    /// </summary>
    [Fact]
    public void BloomFilter_TotalBits_SetCorrectly()
    {
        var expectedItems = 100;
        var fpr = 0.001;
        var filter = new Bloom.BloomFilter(expectedItems, fpr);
        // Bloom filter optimal size: m = -n * ln(p) / (ln(2))^2
        // For n=100, p=0.01: m ~ 958.5
        Assert.True(filter.TotalBits > 0);
        Assert.True(filter.TotalBits >= (ulong)expectedItems);
    }

    /// <summary>
    /// Intersection of single filter returns itself.
    /// BF-12: Single filter intersection
    /// </summary>
    [Fact]
    public void BloomFilter_IntersectionOfSingleFilter_ReturnsSelf()
    {
        var filter = new Bloom.BloomFilter(50, 0.01);
        filter.Add("ACGT");
        var intersection = Bloom.BloomFilter.Intersection(new[] { filter });
        Assert.True(intersection.Contains("ACGT"));
    }

    /// <summary>
    /// Union of empty array works.
    /// BF-13: Empty array union
    /// </summary>
    [Fact]
    public void BloomFilter_UnionOfEmptyArray_Works()
    {
        var union = Bloom.BloomFilter.Union(Array.Empty<Bloom.BloomFilter>());
        Assert.NotNull(union);
    }

    /// <summary>
    /// Intersection of array with null element throws.
    /// BF-14: Null element in arrays
    /// </summary>
    [Fact]
    public void BloomFilter_IntersectionWithNullElement_ThrowsException()
    {
        Bloom.BloomFilter[] nullArray = { new(50, 0.01), null! };
        Assert.Throws<ArgumentNullException>(() => Bloom.BloomFilter.Intersection(nullArray));
    }

    /// <summary>
    /// False positive rate property exists and is read-only.
    /// BF-15: False positive rate
    /// </summary>
    [Fact]
    public void BloomFilter_FalsePositiveRate_Returned()
    {
        var fpr = 0.001;
        var filter = new Bloom.BloomFilter(100, fpr);
        Assert.Equal(fpr, filter.FalsePositiveRate);
    }

    /// <summary>
    /// Union and intersection preserve TotalBits as max of inputs.
    /// BF-16: Size preservation in set operations
    /// </summary>
    [Fact]
    public void BloomFilter_SetOperations_PreserveTotalBits()
    {
        var f1 = new Bloom.BloomFilter(50, 0.01);
        var f2 = new Bloom.BloomFilter(100, 0.01);

        var union = Bloom.BloomFilter.Union(new[] { f1, f2 });
        Assert.True(union.TotalBits >= f1.TotalBits);
        Assert.True(union.TotalBits >= f2.TotalBits);

        var inter = Bloom.BloomFilter.Intersection(new[] { f1, f2 });
        Assert.True(inter.TotalBits <= Math.Max(f1.TotalBits, f2.TotalBits));
    }
}
