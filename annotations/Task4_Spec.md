# Task 4: MultiSampleGraph with BloomFilter Union - Specification

## Goal
Create a `MultiSampleGraph` class that manages multiple `DeBruijnGraph` instances and merges their k-mers using a Bloom filter union for memory-efficient shared k-mer deduplication.

## Classes to Create

### `MultiSampleGraph`
- Holds a collection of sample graphs, each labeled with a sample name
- Union of Bloom filters maintains all k-mers seen across all samples
- Per-sample Bloom filters allow querying whether a k-mer is present in a specific sample
- Methods:
  - `AddSample(string sampleName, DeBruijnGraph graph)` — add a sample
  - `GetUnionFilter()` — return the combined Bloom filter
  - `HasKmerInSample(string sampleName, string kmer)` — check presence in a single sample
  - `HasKmerInAllSamples(IEnumerable<string> sampleNames, string kmer)` — check presence in all named samples
  - `GetUniqueKmersInSample(string sampleName)` — enumerate k-mers unique to one sample
  - `GetSharedKmers(IEnumerable<string> sampleNames)` — enumerate k-mers shared across named samples
  - `BuildUnionGraph()` — construct a new `DeBruijnGraph` from the union of all samples

### `SampleGraph`
- Simple record class holding sample name + graph reference
- Immutable

## Design Decisions

1. Bloom filter parameters: default false positive rate of 0.01, auto-calculated size based on estimated total k-mers
2. Union via Bloom filter: setting1 OR setting2 = bit OR (each position set if EITHER filter has it)
3. Enumeration of shared/unique k-mers: iterate the source sample's k-mers and check membership in other samples' filters
4. Thread safety: not required for initial implementation

## Acceptance Tests

1. `AddSample_AddsGraph` — add a sample and verify it's stored
2. `UnionFilter_ContainsKmersFromAllSamples` — k-mers from any sample are in union
3. `UnionFilter_NotContainsKmersFromNonExistentSample` — k-mers not in any sample are absent from union
4. `HasKmerInSample_True` — k-mer from a sample returns true
5. `HasKmerInSample_False` — k-mer not in a sample returns false
6. `HasKmerInAllSamples_True` — k-mer present in all named samples
7. `HasKmerInAllSamples_False` — k-mer absent from at least one named sample
8. `GetUniqueKmersInSample` — returns k-mers not present in any other sample
9. `GetSharedKmers` — returns k-mers present in all requested samples
10. `BuildUnionGraph` — produces a valid graph with merged k-mers
11. `EmptyMultiSampleGraph_ThrowsOnUnion` — adding no samples and calling union methods handles gracefully
12. `UnionFilter_Size_GrowsWithSamples` — adding more samples increases effective coverage
