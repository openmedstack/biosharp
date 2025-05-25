# Proposal 2: Structural Variant Detection via De Bruijn Graph Analysis
# Specification

## Goal
Extend the existing BioSharp De Bruijn graph with algorithms to detect structural variants
(SV) and complex variants by analyzing graph topology — bubbles, tips, and branching patterns.

## Current State
- `DeBruijnGraph` builds nodes from k-mers, edges from overlaps (prefix/suffix of length k-1)
- Simple greedy traversal: find root (inbound==0), follow first outbound edge
- No edge weights/coverage tracking
- No bubble detection, tip detection, or reference comparison
- Graph is mutated during assembly (edges removed)

## Enhancement Specification

### 1. Edge Coverage Tracking
Each edge in the graph tracks how many reads support it (coverage).
- `KmerNode` gains a `Coverage` property (int)
- Edges track coverage: add `Edge` class with `Target` (string), `Coverage` (int), `Valid` (bool, for tip pruning)

### 2. Bubble Detection Algorithm ("BubbleFinder")
A bubble = two or more divergent paths between a shared start and shared end node.
- Classify bubbles as SNPs (1-3nt difference), small indels (4-14nt), or SVs (>=15nt)
- Output: `Bubble` record with path variants, types, lengths, coverage for each path

Algorithm:
1. Find all nodes with branching factor >= 1 (one inbound, multiple outbound) — branching points
2. For each branching node, do a DFS/BFS to find where branches re-converge (single inbound, multiple outbound or >= 2 inbound, 1 outbound)
3. Extract each path between branching points, trim common prefixes/suffixes
4. Classify the variant by length difference between paths

### 3. Tip Detection ("TipFinder")
Tips = dead-end branches. Long tips (>2*k) represent potential insertions.
- During graph traversal, mark nodes not visited as tips
- Classify tips: short tips (<=2*k) are likely sequencing errors, long tips (>>2*k) are insertions

### 4. Reference Comparison
Compare alt paths from bubbles/tips against a reference sequence to:
- Determine absolute position on reference
- Classify as INSERTION (alt path longer), DELETION (alt path shorter), or SUBSTITUTION (same length, different bases)
- Compute coverage ratio (alt coverage / ref coverage) for quality scoring

### 5. Variant Output
Produce `LocalVariantResult` compatible with existing variant caller output:
- Chromosome, position, reference, alternate, quality score
- `IsStructuralVariant` flag, `SvType` (INSERTION, DELETION, SUBSTITUTION)

## File Changes
- `src/openmedstack.biosharp.calculations/DeBruijn/Edge.cs` — new: edge with coverage
- `src/openmedstack.biosharp.calculations/DeBruijn/KmerNode.cs` — extends: coverage, edge list with coverage
- `src/openmedstack.biosharp.calculations/DeBruijn/BubbleFinder.cs` — new: bubble detection
- `src/openmedstack.biosharp.calculations/DeBruijn/TipFinder.cs` — new: tip detection
- `src/openmedstack.biosharp.calculations/DeBruijn/PathComparator.cs` — new: alt path vs reference
- `src/openmedstack.biosharp.calculations/DeBruijn/StructuralVariantDetector.cs` — new: orchestrator
- `tests/openmedstack.biosharp.calculations.tests/DeBruijnSvDetectionTests.cs` — new: acceptance tests
