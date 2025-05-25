# VariantAnnotator — Design and Annotation Flow

## Architecture Overview

`VariantAnnotator` is a **static utility class** in `OpenMedStack.BioSharp.Calculations` that classifies genetic variants into biological consequence categories (missense, nonsense, frameshift, synonymous, splice site, etc.). It is intentionally **decoupled from VCF parsing** — it knows nothing about `VcfVariant` and operates purely on DNA sequences, codon changes, and positional metadata.

## The Annotate Method Signature

```csharp
public static VariantAnnotation? Annotate(
    CodonChange codonChange,      // Pre-computed DNA-level codon mutation
    string transcriptId,          // Transcript identifier (e.g. "NM_001")
    Sequence transcriptSequence,  // The full transcript DNA sequence
    int cPosition,                // 1-based genomic/coding position
    char? refBase = null,         // Reference allele (first character)
    char? altBase = null)         // Alternate allele (first character)
```

The method does **not** accept a `VcfVariant` instance. Instead, it receives **flattened primitive values** extracted from a `VcfVariant` at the call site.

## Annotation Flow

### 1. Caller Deconstructs VcfVariant

The `VcfAnnotationEngine` (in `VariantAnnotationEngine.cs`) is responsible for reading VCF files, iterating over variants, and calling `VariantAnnotator`. It extracts the relevant fields first:

```csharp
// In VariantAnnotationEngine.AnnotateSingleVariantAgainstTranscript
var altAlleles = variant.Alternate.Split(',');
foreach (var altAllele in altAlleles)
{
    var refAllele = variant.Reference;
    var cPos = variant.Position;                    // 1-based position
    var codonChange = BuildCodonChange(variant, altAllele, transcript);
    var ann = VariantAnnotator.Annotate(
        codonChange,
        transcript.Id,
        transcript,
        cPos,
        refAllele[0],
        altAllele[0],
        _annotationContext);
}
```

### 2. CodonChange is Pre-Computed

`BuildCodonChange()` determines the type of change (substitution, deletion, insertion, multi-base deletion) and produces a `CodonChange` record:

```csharp
public record CodonChange
{
    public string OriginalCodon { get; init; }  // DNA before mutation
    public string MutatedCodon { get; init; }   // DNA after mutation
    public int NucleotideDelta { get; init; }   // Length change (positive = insertion)
}
```

Supported constructor helpers in `VariantAnnotator`:

| Variant Type | Helper Method |
|---|---|
| Single-nucleotide substitution | `Substitution(refCodon, cPos, refBase, altBase)` |
| Single-base deletion | `Deletion(refCodon, cPos, delBase)` |
| Single-base insertion | `Insertion(refCodon, cPos, insBase)` |
| Multi-base deletion (1–3 bp) | `MultiDeletion(refCodon, cPos, basesToDelete)` |
| Multi-codon indel | `MultiCodonIndel(refSeq, cPos, refSubset, altSeq)` |
| Multi-nucleotide polymorphism | `Mnp(refCodons, positions, altBases)` |
| Deletion-insertion | `Delins(refCodon, cPos, basesToDelete, insertionBases)` |

### 3. Annotation Processing (inside `Annotate`)

Once invoked, `Annotate` performs these steps:

1. **Extract first 3 bases** from `OriginalCodon` and `MutatedCodon` (for multi-codon variants like MNP or large indels, only the first codon is translated).
2. **Translate to RNA** using `CodonToRna` (replaces T with U).
3. **Translate to amino acids** using `TryTranslate` (looks up the codon table).
4. **Classify consequence** by calling `ClassifyConsequenceWithOffset`:
    - If `AnnotationContext` is present, `ClassifyPosition` is checked first for non-coding regions (splice site, upstream, downstream, intronic, intergenic, UTR).
    - Otherwise, consequence is determined by comparing old vs. new amino acids and the nucleotide delta:
        - Delta not divisible by 3 → `Frameshift`
        - New amino acid is `*` (stop) → `Nonsense`
        - Same amino acid → `Synonymous`
        - Different amino acid → `Missense`
        - Stop in last codon → `StopRetained`
        - Indel with delta divisible by 3 → `InframeDeletion` or `InframeInsertion`
5. **Calculate FrameshiftOffset** (if frameshift): counts amino acids from the shift position to the nearest stop codon in the mutated sequence, or `-1` if no stop codon is found within 600 bases.
6. **Build HGVS strings** via `BuildProteinHgvs` (e.g. `p.Met1Val`, `p.Ala10del`, `p.Val3fs*`) and `BuildHgvsCoding`.
7. **Return** a `VariantAnnotation` record, or `null` if translation fails.

### 4. Output

```csharp
public record VariantAnnotation
{
    public string AffectedGene { get; init; }        // Transcript ID
    public VariantConsequence Consequence { get; init; }
    public string? HgvsCoding { get; init; }          // c. notation
    public string? HgvsProtein { get; init; }         // p. notation
    public AminoAcid? AffectedAminoAcid { get; init; }
    public AminoAcid? ResultingAminoAcid { get; init; }
    public string? CodonChange { get; init; }         // e.g. "ATG>GTG"
    public int? FrameshiftOffset { get; init; }       // null for non-frameshift
}
```

## Why No VcfVariant Parameter?

This is a **deliberate design choice**. `VariantAnnotator` intentionally has zero dependency on `OpenMedStack.BioSharp.Model.Vcf`. The reasons:

1. **Input agnosticism** — `VariantAnnotator` works with any source of variant data. It could be a VCF, BAM, FASTQ variant caller, or synthetic test data. Removing the VCF dependency keeps it portable across the codebase.

2. **Single responsibility** — VCF parsing is handled by `VcfFileReader`. Variant filtering and orchestration is handled by `VariantAnnotationEngine`. `VariantAnnotator` focuses purely on biological consequence classification. Each layer has one concern.

3. **Multi-allele support** — A single `VcfVariant.Alternate` can contain comma-separated alleles (e.g., `ALT="A,T"`). The engine splits these before calling `Annotate`, which only processes one codon change at a time. If `Annotate` accepted a full `VcfVariant`, it would need to handle allele splitting internally, blurring the boundary.

4. **Testability** — Unit tests can construct `CodonChange` objects by hand without mocking or constructing a full `VcfVariant`. This is used extensively in the acceptance test suite (AC1.1–AC3.8) where raw DNA sequences and positions are passed directly.

5. **Extensibility** — Adding a new input format (e.g., gVCF, JSON API response) only requires a new orchestrator that maps to `CodonChange` + primitives. The annotation logic itself never changes.

## Test Coverage

All annotation paths are covered by acceptance tests in `AcceptanceTests.cs`:

| Test Group | AC Reference | Scope |
|---|---|---|
| Non-coding classification | AC2.1–AC2.9 | SpliceSite, Upstream, Downstream, Intronic, Intergenic, UTR, CDS boundary, position validation |
| Complex variant builders | AC3.1–AC3.6 | MultiCodonIndel, Mnp, Delins (basic, single-base, expansion, pure deletion, out-of-bounds) |
| Frameshift offset | AC3.7–AC3.8 | Stop codon detection, -1 for no stop, positive count with stop, inframe change leaves null |
| Integration | Extras | ClassifyConsequence calls ClassifyPosition end-to-end |

Total: 47 acceptance tests, all passing, covering all 3 proposals.
