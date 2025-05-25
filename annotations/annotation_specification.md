# Variant Annotation Engine Improvement Specification

## Overview

This specification covers three proposals from `annotation_improvements.md` to enhance the VariantAnnotator engine:
1. A coordination layer (`VariantAnnotationEngine`) that bridges VCF reading, transcript FASTA loading, and annotation
2. Non-coding consequence types (SpliceSite, Upstream, Downstream, Intronic, VariantInUtr)
3. Complex variant support (MultiCodonIndel, Mnp, Delins) and FrameshiftOffset calculation

## Proposal 1: VariantAnnotationEngine

### Purpose
Provide a high-level API to annotate VCF files against transcript sequences without requiring the caller to wire VcfFileReader + FASTA reader + VariantAnnotator together.

### New Class: `VariantAnnotationEngine`
File: `src/openmedstack.biosharp.calculations/VariantAnnotationEngine.cs`
Namespace: `OpenMedStack.BioSharp.Calculations` (same as `VariantAnnotator`)

### API

```csharp
public class VariantAnnotationEngine : IDisposable
{
    // Load transcripts from a FASTA file, keyed by transcript ID
    public Task LoadTranscriptsAsync(string fastaPath, CancellationToken ct = default);
    
    // Annotate all variants in a VCF file
    public IAsyncEnumerable<VariantAnnotation> AnnotateVcfAsync(
        string vcfPath,
        string? transcriptId = null,
        float minQuality = 5.0f,
        CancellationToken ct = default);
    
    // Annotate a single VcfVariant against loaded transcripts
    public VariantAnnotation[]? AnnotateVariantAsync(VcfVariant variant);
    
    // Dispose underlying readers
    public void Dispose();
}
```

### Requirements
- R1.1: `LoadTranscriptsAsync` reads a FASTA file and stores sequences in a `Dictionary<string, Sequence>` keyed by sequence ID (the part after `>` in FASTA header, or extracted from `>NM_004006.3` style headers)
- R1.2: `AnnotateVcfAsync` iterates VCF records, filters by `minQuality` using the `ErrorProbabilities` field (phred-scaled: phred >= minQuality passes), filters by optional `transcriptId`
- R1.3: For each passing variant, calls `VariantAnnotator.AnnotateAll()` with the stored transcript
- R1.4: `AnnotateVariantAsync` annotates a single variant against all available transcripts
- R1.5: Supports one or more transcripts; if none match a variant, the variant is silently skipped
- R1.6: IDisposable pattern - closes any open readers

### Acceptance Criteria (AC1)
- AC1.1: Loading a FASTA with 2 transcripts produces a dictionary with 2 entries
- AC1.2: Annotating a VCF with 3 variants against 1 transcript produces 3 annotations (if all pass quality filter)
- AC1.3: Filtering by transcriptId returns only annotations for that transcript
- AC1.4: Variants below minQuality are skipped
- AC1.5: AnnotateVariantAsync returns annotations for all loaded transcripts
- AC1.6: Loading a non-existent file throws `FileNotFoundException`
- AC1.7: Disposing the engine closes underlying resources

## Proposal 2: SpliceSite and Non-Coding Region Annotations

### Purpose
Implement the 5 consequence types that exist in `VariantConsequence` enum but are never produced: SpliceSite, Upstream, Downstream, Intronic, VariantInUtr.

### New Class: `AnnotationContext`
File: `src/openmedstack.biosharp.model/AnnotationContext.cs`
Namespace: `OpenMedStack.BioSharp.Model`

```csharp
public record AnnotationContext
{
    public int CdsStart { get; init; }      // 1-based, inclusive
    public int CdsEnd { get; init; }        // 1-based, inclusive
    public int TranscriptLength { get; init; }
    
    public static AnnotationContext FromCdsBoundaries(int cdsStart, int cdsEnd, int transcriptLength)
    {
        return new AnnotationContext
        {
            CdsStart = cdsStart,
            CdsEnd = cdsEnd,
            TranscriptLength = transcriptLength
        };
    }
    
    // Returns the consequence category for a position in the transcript
    public VariantConsequence ClassifyPosition(int position)
    {
        const int spliceWindow = 3;
        const int regionWindow = 3000;
        
        // Splice site: within 3bp of exon-intron boundary
        if (position >= CdsStart - spliceWindow && position < CdsStart)
            return VariantConsequence.SpliceSite;
        if (position > CdsEnd && position <= CdsEnd + spliceWindow)
            return VariantConsequence.SpliceSite;
        
        // UTR: between transcript start and CDS, or between CDS end and transcript end
        if (position >= 1 && position < CdsStart)
            return VariantConsequence.VariantInUtr;
        if (position > CdsEnd && position <= TranscriptLength)
            return VariantConsequence.VariantInUtr;
        
        // Upstream: within 3kb before CDS start
        if (position < CdsStart && CdsStart - position <= regionWindow)
            return VariantConsequence.Upstream;
        
        // Downstream: within 3kb after CDS end
        if (position > CdsEnd && position - CdsEnd <= regionWindow)
            return VariantConsequence.Downstream;
        
        // Coding region: inside CDS
        if (position >= CdsStart && position <= CdsEnd)
            return VariantConsequence.Intronic;  // coding position, will be classified by existing logic
        
        return VariantConsequence.Intergenic;
    }
}
```

### Changes to `VariantAnnotator.ClassifyConsequence`
- Add a parameter `AnnotationContext? context`
- When context is provided and the position falls in a non-coding region, return the appropriate non-coding consequence BEFORE the existing coding-variant logic
- When context is provided and position is in-coding, proceed with existing classification
- When context is null, behavior is unchanged (all positions treated as coding)

### Acceptance Criteria (AC2)
- AC2.1: Position 1000 with CDS 1003-2000 returns SpliceSite (within 3bp upstream of CDS)
- AC2.2: Position 3003 with CDS 1-3000 returns SpliceSite (within 3bp downstream of CDS)
- AC2.3: Position 500 with CDS 1000-2000, transcript length 5000 returns VariantInUtr (5' UTR)
- AC2.4: Position 2500 with CDS 1000-2000, transcript length 5000 returns VariantInUtr (3' UTR)
- AC2.5: Position 9970 with CDS 10000-12000 returns Upstream (within 3kb)
- AC2.6: Position 12010 with CDS 10000-12000 returns Downstream (within 3kb)
- AC2.7: Position 9500 with CDS 10000-12000 returns Intronic (in intron, outside splice window)
- AC2.8: Position 15000 with CDS 10000-12000, transcript length 20000 returns Intergenic (beyond 3kb downstream)
- AC2.9: Passing null context returns original coding-region behavior for all positions

## Proposal 3: Complex Variant Support

### 3.1: MultiCodonIndel Builder
File: `src/openmedstack.biosharp.calculations/VariantAnnotator.cs` (add method)

```csharp
/// <summary>
/// Build a CodonChange for a multi-base indel spanning multiple codons.
/// </summary>
/// <param name="refSeq">Reference sequence (at least as long as refSubset)</param>
/// <param name="cPos">1-based position of the first affected nucleotide</param>
/// <param name="refSubset">The reference bases at the variant position</param>
/// <param name="altSeq">The alternate bases inserted at this position</param>
public static CodonChange? MultiCodonIndel(
    string refSeq,
    int cPos,
    string refSubset,
    string altSeq);
```

Behavior:
- Extract affected codons: determine which codons contain the variant position
- Construct the original codon sequence (all affected codons concatenated)
- Construct the mutated codon sequence (refSubset replaced by altSeq)
- Return CodonChange containing the full affected region

### 3.2: MNP (Multi-Nucleotide Polymorphism) Builder
```csharp
/// <summary>
/// Build a CodonChange for a multi-nucleotide polymorphism (multiple single-base substitutions
/// occurring at adjacent positions within the same codon or spanning two codons).
/// </summary>
/// <param name="refCodons">Reference codon(s) containing the variant</param>
/// <param name="positions">1-based positions of each substituted base</param>
/// <param name="altBases"> Alternate bases at those positions (one per position)</param>
public static CodonChange? Mnp(
    string refCodons,
    IReadOnlyList<int> positions,
    IReadOnlyList<char> altBases);
```

Behavior:
- Validate each position is within the codon(s) range
- Apply all substitutions in parallel (not sequentially, so order doesn't matter)
- Return CodonChange with original and mutated codon strings
- If substitutions span two codons, both are included in result

### 3.3: Delins (Deletion-Insertion) Builder
```csharp
/// <summary>
/// Build a CodonChange for a compound deletion-insertion event.
/// </summary>
/// <param name="refCodon">Reference codon</param>
/// <param name="cPos">1-based start position</param>
/// <param name="basesToDelete">Number of bases to delete</param>
/// <param name="insertionBases">Bases to insert in place of the deleted ones</param>
public static CodonChange? Delins(
    string refCodon,
    int cPos,
    int basesToDelete,
    string insertionBases);
```

Behavior:
- Delete the specified number of bases from refCodon starting at cPos
- Insert insertionBases at the same position
- Return CodonChange with original and mutated codon
- Handle cases where deletion + insertion spans more than one codon

### 3.4: FrameshiftOffset Calculation
Wire `CountAminosUntilStop` into `ClassifyConsequence` when consequence is Frameshift:

- When consequence is Frameshift, call `CountAminosUntilStop` and store result in `FrameshiftOffset`
- If amino acid sequence after shift is empty/insufficient, FrameshiftOffset = -1
- Otherwise, FrameshiftOffset = number of amino acids until the new stop codon

### Acceptance Criteria (AC3)
- AC3.1: MultiCodonIndel("ATGATG", 1, "ATGATG", "ATGACG") produces OriginalCodon="ATGATG", MutatedCodon="ATGACG"
- AC3.2: MultiCodonIndel with 4bp deletion spanning 2 codons produces codonChange covering both
- AC3.3: Mnp("ATG", new[]{2,3}, new[]{'C','G'}) returns original="ATG", mutated="ACG"
- AC3.4: Mnp with positions in two adjacent codons spans both codons in result
- AC3.5: Delins("ATGGC", 1, 3, "CAG") deletes ATG from beginning and inserts CAG, producing "CAGGC"
- AC3.6: Delins handles insertion longer than deletion (expanding indel)
- AC3.7: Frameshift consequence always sets FrameshiftOffset to a non-null value
- AC3.8: FrameshiftOffset correctly counts amino acids until stop codon
