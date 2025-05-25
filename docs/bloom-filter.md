# Bloom Filter

## Introduction

When comparing genomic datasets or filtering k-mers, you need a data structure that can test membership ("does this 
k-mer appear in dataset X?") without storing every k-mer in memory. A Bloom filter is a probabilistic data structure 
that answers this question efficiently using a compressed bit array. In genomic workflows, Bloom filters are primarily 
used for [somatic variant detection](somatic-variant-detection.md): by building a Bloom filter from a normal (germline) sample's k-mers and 
another from a tumor sample, you can quickly identify tumor-specific k-mers that represent somatic mutations rather 
than inherited germline variation. [Bloom filters](bloom-filter.md) are also used in [repeat masking](repeat-masking.md) for fast pre-filtering 
of known repeat sequences during assembly and variant calling.

BioSharp implements a standard Bloom filter using two hash functions (FNV-1a) and Rook's method for efficient multiple 
hash function simulation. It uses a bit array to track which k-mers have been seen. When a k-mer is added, all its hash 
positions are set to 1. When queried, the filter checks if all those positions are 1.

Bloom filters have an important trade-off: they can return false positives (saying a k-mer exists when it does not) but 
never false negatives. This makes them ideal for pre-filtering — you can quickly eliminate k-mers that are definitely 
absent, then do more expensive checks on the candidates that pass.

In BioSharp, Bloom filters enable multi-sample analysis. By building a "normal" Bloom filter from control sample k-mers 
and a "tumor" Bloom filter from tumor k-mers, you can quickly identify tumor-specific k-mers that should be flagged as 
somatic variants rather than germline variation.

```csharp
// Build a Bloom filter with expected 100,000 k-mers and 0.1% false-positive rate
var filter = new BloomFilter(expectedInsertions: 100_000, targetFpr: 0.001);

// Add k-mers
var kmer = "ACGTTACG";
filter.Add(kmer);
filter.Add(new[] { "ACGTTACG", "CGTTACGT", "GTTACGTA" });

// Query
bool present = filter.Contains("ACGTTACG");      // true (definitely)
bool absent   = filter.Contains("ZZZ");            // false (definitely)
bool maybe    = filter.Contains("RANDOMBASE");     // possibly true (false positive)

// Bloom filter metadata
Console.WriteLine($"Bits: {filter.TotalBits}");
Console.WriteLine($"Hashes: {filter.NumHashFunctions}");
Console.WriteLine($"Estimated FPR: {filter.EstimatedFprValue:F6}");
```

**Set operations** for multi-sample analysis are the backbone of the [Multi-Sample Graph](multi-sample-graph.md) infrastructure and the 
[somatic detection](somatic-variant-detection.md) pipeline:

```csharp
// Union: a k-mer is in the result if it is in ANY sample
var allSamplesFilter = BloomFilter.Union(new[] { sample1Filter, sample2Filter, sample3Filter });

// Intersection: a k-mer is in the result only if it is in ALL samples (common k-mers)
var commonKmers = BloomFilter.Intersection(new[] { sample1Filter, sample2Filter, sample3Filter });
```
