# Multi-Sample Graph Management

## Introduction

When analyzing multiple samples, you need a structure that manages multiple [De Bruijn graphs](de-bruijn-assembly.md) (one per sample) 
and provides utilities for comparing them. `MultiSampleGraph` serves this purpose, forming the backbone of the 
[somatic detection](somatic-variant-detection.md) and [cohort analysis](cohort-variant-calling.md) workflows.

Each sample's reads are assembled into its own graph, stored under a sample name. The class provides operations to 
compare graphs: finding k-mers unique to one sample, k-mers shared between samples, and a combined filter covering all 
samples.

The **union filter** represents all k-mers present in any sample — useful for building a comprehensive reference of 
what exists across the cohort. The **unique k-mers in sample X** represents k-mers exclusive to that sample, which are 
candidates for somatic or private germline variants. The **shared k-mers** between samples represent common genetic 
elements present in all compared samples.

These utilities feed directly into somatic and cohort calling: you build a graph per sample, add the normal graph plus 
all tumor graphs, then use the shared/unique k-mer calculations to drive variant detection.

The process involves:
1. Creating a MultiSampleGraph with a chosen k-mer size
2. Adding normal and tumor graphs under sample identifiers
3. Building a union Bloom filter across all samples
4. Finding k-mers unique to tumor (somatic candidates)
5. Finding shared k-mers (germline/common candidates)

```csharp
var multiGraph = new MultiSampleGraph(k: 15);
multiGraph.AddSample("tumor", tumorGraph);
multiGraph.AddSample("normal", normalGraph);

// Build a union Bloom filter of all samples
var allKmerFilter = await multiGraph.GetUnionFilterAsync();

// Find k-mers unique to tumor (not in normal)
var tumorUnique = await multiGraph.GetUniqueKmersInSampleAsync("tumor");

// Find shared k-mers
var shared = await multiGraph.GetSharedKmersAsync(new[] { "tumor", "normal" });
```
