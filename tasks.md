# BioSharp DeBruijn Improvement Tasks

All tasks are derived from `annotations/debruijn_improvements.md` proposal.

## Task 1: BubbleConfidence Enum
**Proposal 1:** Confidence classification for bubbles.
- Create `BubbleConfidence` enum: `High`, `Medium`, `Low`
- Tests: `BubbleConfidenceTests.cs`
- File: `src/openmedstack.biosharp.calculations/DeBruijn/BubbleConfidence.cs`

## Task 2: RepetitivenessAnalyzer
**Proposal 1:** Count k-mer frequencies to score path repetitiveness.
- Create `RepetitivenessAnalyzer` helper class
- `AnalyzeBubble` will use it for confidence scoring
- Tests: `RepetitivenessAnalyzerTests.cs`
- File: `src/openmedstack.biosharp.calculations/DeBruijn/RepetitivenessAnalyzer.cs`

## Task 3: Integrate Confidence Scoring into AnalyzeBubble
**Proposal 1:** Use repetitiveness scores + coverage ratios in bubble analysis.
- Add `BubbleConfidence` to `Bubble` class
- Compute confidence: `(refCoverage + altCoverage) / (repeatCount * k)`
- Mark high-copy repeat bubbles as `Low` confidence
- Update tests in `DeBruijnSvDetectionTests.cs`

## Task 4: MultiSampleGraph - K-mer Union with BloomFilter
**Proposal 3:** Union k-mers across multiple graphs using BloomFilter.
- Create `MultiSampleGraph` class
- Supports adding multiple `DeBruijnGraph` instances
- Merges via BloomFilter union for memory-efficient k-mer dedup
- Tests: `MultiSampleGraphTests.cs`
- File: `src/openmedstack.biosharp.calculations/DeBruijn/MultiSampleGraph.cs`

## Task 5: Somatic Calling Pipeline
**Proposal 3:** Tumor-normal pair support.
- Variant detection: bubble in tumor absent from normal BloomFilter = somatic
- Integration test in `SomaticCallingTests.cs`
- File: `src/openmedstack.biosharp.calculations/DeBruijn/SomaticVariantDetector.cs`

## Task 6: Cohort Calling Pipeline
**Proposal 3:** 10-100 sample cohort with shared variant catalog.
- Common variants (present in multiple samples) get higher confidence
- Integration test in `CohortCallingTests.cs`
- File: `src/openmedstack.biosharp.calculations/DeBruijn/CohortVariantCaller.cs`

## Task 7: repeats.json K-mer Masking
**Proposal 1:** Load known problematic k-mers from JSON config.
- Create `repeats.json` data file
- Masking during graph building and bubble analysis
- Tests: `RepeatMaskingTests.cs`
- Files: `data/repeats.json`, `src/.../DeBruijn/RepeatMasker.cs`

## Task 8: Full Integration Tests ✅ **COMPLETE**
End-to-end tests combining all features.
- Tests: `FullPipelineTests.cs`
- Scenario: normal + tumor samples with BloomFilter filtering, genotype calling, VCF output

## Task 9: Commit Everything ✅ **COMPLETE**

All tasks completed. 419 tests pass.
Commit all changes to git when all tests pass.
