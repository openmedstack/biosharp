using System;
using System.Collections.Generic;
using System.Linq;
using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Vcf;

namespace OpenMedStack.BioSharp.Calculations;

/// <summary>
/// Annotates genetic variants with predicted biological consequences.
/// Translates between HGVS c. (coding DNA) and p. (protein) notation,
/// and classifies variants into consequence categories.
/// </summary>
public static class VariantAnnotator
{
    private static readonly Dictionary<string, AminoAcid> CodonTable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UUU"] = new AminoAcid('F', "Phenylalanine"),
        ["UUC"] = new AminoAcid('F', "Phenylalanine"),
        ["UUA"] = new AminoAcid('L', "Leucine"),
        ["UUG"] = new AminoAcid('L', "Leucine"),
        ["CUU"] = new AminoAcid('L', "Leucine"),
        ["CUC"] = new AminoAcid('L', "Leucine"),
        ["CUA"] = new AminoAcid('L', "Leucine"),
        ["CUG"] = new AminoAcid('L', "Leucine"),
        ["AUU"] = new AminoAcid('I', "Isoleucine"),
        ["AUC"] = new AminoAcid('I', "Isoleucine"),
        ["AUA"] = new AminoAcid('I', "Isoleucine"),
        ["AUG"] = new AminoAcid('M', "Methionine"),
        ["GUU"] = new AminoAcid('V', "Valine"),
        ["GUC"] = new AminoAcid('V', "Valine"),
        ["GUA"] = new AminoAcid('V', "Valine"),
        ["GUG"] = new AminoAcid('V', "Valine"),
        ["UCU"] = new AminoAcid('S', "Serine"),
        ["UCC"] = new AminoAcid('S', "Serine"),
        ["UCA"] = new AminoAcid('S', "Serine"),
        ["UCG"] = new AminoAcid('S', "Serine"),
        ["AGU"] = new AminoAcid('S', "Serine"),
        ["AGC"] = new AminoAcid('S', "Serine"),
        ["CCU"] = new AminoAcid('P', "Proline"),
        ["CCC"] = new AminoAcid('P', "Proline"),
        ["CCA"] = new AminoAcid('P', "Proline"),
        ["CCG"] = new AminoAcid('P', "Proline"),
        ["ACU"] = new AminoAcid('T', "Threonine"),
        ["ACC"] = new AminoAcid('T', "Threonine"),
        ["ACA"] = new AminoAcid('T', "Threonine"),
        ["ACG"] = new AminoAcid('T', "Threonine"),
        ["GCU"] = new AminoAcid('A', "Alanine"),
        ["GCC"] = new AminoAcid('A', "Alanine"),
        ["GCA"] = new AminoAcid('A', "Alanine"),
        ["GCG"] = new AminoAcid('A', "Alanine"),
        ["UAU"] = new AminoAcid('Y', "Tyrosine"),
        ["UAC"] = new AminoAcid('Y', "Tyrosine"),
        ["CAU"] = new AminoAcid('H', "Histidine"),
        ["CAC"] = new AminoAcid('H', "Histidine"),
        ["CAA"] = new AminoAcid('Q', "Glutamine"),
        ["CAG"] = new AminoAcid('Q', "Glutamine"),
        ["AAU"] = new AminoAcid('N', "Asparagine"),
        ["AAC"] = new AminoAcid('N', "Asparagine"),
        ["AAA"] = new AminoAcid('K', "Lysine"),
        ["AAG"] = new AminoAcid('K', "Lysine"),
        ["GAU"] = new AminoAcid('D', "Aspartic Acid"),
        ["GAC"] = new AminoAcid('D', "Aspartic Acid"),
        ["GAA"] = new AminoAcid('E', "Glutamic Acid"),
        ["GAG"] = new AminoAcid('E', "Glutamic Acid"),
        ["UGU"] = new AminoAcid('C', "Cysteine"),
        ["UGC"] = new AminoAcid('C', "Cysteine"),
        ["UGG"] = new AminoAcid('W', "Tryptophan"),
        ["CGU"] = new AminoAcid('R', "Arginine"),
        ["CGC"] = new AminoAcid('R', "Arginine"),
        ["CGA"] = new AminoAcid('R', "Arginine"),
        ["CGG"] = new AminoAcid('R', "Arginine"),
        ["AGA"] = new AminoAcid('R', "Arginine"),
        ["AGG"] = new AminoAcid('R', "Arginine"),
        ["GGU"] = new AminoAcid('G', "Glycine"),
        ["GGC"] = new AminoAcid('G', "Glycine"),
        ["GGA"] = new AminoAcid('G', "Glycine"),
        ["GGG"] = new AminoAcid('G', "Glycine"),
        ["UAA"] = new AminoAcid('*', "Nonsense"),
        ["UAG"] = new AminoAcid('*', "Nonsense"),
        ["UGA"] = new AminoAcid('*', "Nonsense")
    };

    extension(CodonChange codonChange)
    {
        /// <summary>
        /// Annotates a single VCF variant against a transcript.
        /// </summary>
        public VariantAnnotation? Annotate(
            string transcriptId,
            Sequence transcriptSequence,
            int cPosition,
            char? refBase = null,
            char? altBase = null)
        {
            return codonChange.Annotate(transcriptId, transcriptSequence, cPosition, refBase, altBase, null);
        }

        /// <summary>
        /// Annotates a single VCF variant against a transcript, with optional annotation context for
        /// non-coding consequence classification.
        /// </summary>
        public VariantAnnotation? Annotate(
            string transcriptId,
            Sequence transcriptSequence,
            int cPosition,
            char? refBase,
            char? altBase,
            AnnotationContext? context)
        {
            if (string.IsNullOrWhiteSpace(transcriptId))
            {
                throw new ArgumentException("Transcript ID must be provided.", nameof(transcriptId));
            }

            if (codonChange == null)
            {
                throw new ArgumentNullException(nameof(codonChange));
            }

            if (transcriptSequence == null)
            {
                throw new ArgumentNullException(nameof(transcriptSequence));
            }

            // If refBase is provided, verify it matches the transcript at the given position.
            // A mismatch means the VCF record's reference allele doesn't agree with the
            // transcript reference, so the annotation is marked Uncertain.
            if (refBase != null && TranscriptRefMismatch(refBase.Value, transcriptSequence, cPosition, codonChange))
            {
                var uncertainHgvsProtein = "p.?";
                var uncertainHgvsCoding = BuildHgvsCoding(cPosition, codonChange, refBase, altBase);
                return new VariantAnnotation
                {
                    AffectedGene = transcriptId,
                    Consequence = VariantConsequence.Uncertain,
                    HgvsCoding = uncertainHgvsCoding,
                    HgvsProtein = uncertainHgvsProtein,
                    AffectedAminoAcid = null,
                    ResultingAminoAcid = null,
                    CodonChange = codonChange.OriginalCodon + ">" + codonChange.MutatedCodon,
                    FrameshiftOffset = null
                };
            }

            var rawOriginal = codonChange.OriginalCodon;
            var rawMutated = codonChange.MutatedCodon;

            // For multi-codon variants, extract the first codon for translation
            var originalCodon = rawOriginal.Length > 3 ? rawOriginal[..3] : rawOriginal;
            var mutatedCodon = rawMutated.Length > 3 ? rawMutated[..3] : rawMutated;

            var oldCodonRna = CodonToRna(originalCodon);
            var newCodonRna = CodonToRna(mutatedCodon);

            var oldAa = TryTranslate(oldCodonRna);
            var newAa = TryTranslate(newCodonRna);

            if (oldAa == null)
            {
                return null;
            }

            var (consequence, frameshiftOffset) = ClassifyConsequenceWithOffset(oldAa, newAa,
                codonChange.NucleotideDelta,
                transcriptSequence, codonChange, cPosition, context);
            var hgvsProtein = BuildProteinHgvs(consequence, oldAa, newAa, cPosition, codonChange);
            var hgvsCoding = BuildHgvsCoding(cPosition, codonChange, refBase, altBase);

            return new VariantAnnotation
            {
                AffectedGene = transcriptId,
                Consequence = consequence,
                HgvsCoding = hgvsCoding,
                HgvsProtein = hgvsProtein,
                AffectedAminoAcid = oldAa,
                ResultingAminoAcid = newAa,
                CodonChange = codonChange.OriginalCodon + ">" + codonChange.MutatedCodon,
                FrameshiftOffset = frameshiftOffset
            };
        }
    }

    /// <summary>
    /// Annotates all alt alleles from a VCF variant.
    /// </summary>
    public static IEnumerable<VariantAnnotation> AnnotateAll(
        this VcfVariant variant,
        Func<VcfVariant, Sequence, int, string, CodonChange?> codonChangeFactory,
        Sequence transcriptSequence,
        string transcriptId)
    {
        var altAlleles = variant.Alternate.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var altAllele in altAlleles)
        {
            if (altAllele == "*" || altAllele == variant.Reference || altAllele.StartsWith('<'))
            {
                continue;
            }

            var refAllele = variant.Reference;
            var cPos = variant.Position;

            if (refAllele == altAllele)
            {
                continue;
            }

            var codonChange = codonChangeFactory(variant, transcriptSequence, cPos, altAllele);

            var ann = codonChange?.Annotate(transcriptId, transcriptSequence, cPos, refAllele[0],
                altAllele[0]);
            if (ann != null)
            {
                yield return ann;
            }
        }
    }

    private static VariantConsequence ClassifyConsequence(
        AminoAcid oldAa,
        AminoAcid? newAa,
        int nucleotideDelta,
        Sequence transcript,
        int pos,
        AnnotationContext? context = null)
    {
        // An empty transcript provides no reference -- classify as unknown.
        if (transcript.Length == 0)
        {
            return VariantConsequence.Unknown;
        }

        if (pos < 1)
        {
            return VariantConsequence.Unknown;
        }

        // If AnnotationContext is provided, classify non-coding positions first
        var nonCoding = context?.ClassifyPosition(pos);
        if (nonCoding.HasValue)
        {
            return nonCoding.Value;
        }

        var checkPos = nucleotideDelta >= 0
            ? pos + nucleotideDelta - 1
            : pos - 1;

        // Out-of-bounds: return Unknown instead of crashing
        if (checkPos > transcript.Length)
        {
            return VariantConsequence.Unknown;
        }

        // StopRetained: nonsense mutation in the very last codon (within 3bp of transcript end)
        // is technically functional since the protein isn't truncated prematurely.
        if (newAa is { Letter: '*' })
        {
            // Last codon starts at this position (1-based)
            var lastCodonStart = (transcript.Length - 1) / 3 * 3 + 1;
            return checkPos >= lastCodonStart ? VariantConsequence.StopRetained : VariantConsequence.Nonsense;
        }

        if (nucleotideDelta != 0 && nucleotideDelta % 3 != 0)
        {
            return VariantConsequence.Frameshift;
        }

        if (nucleotideDelta != 0)
        {
            return nucleotideDelta < 0
                ? VariantConsequence.InframeDeletion
                : VariantConsequence.InframeInsertion;
        }

        if (oldAa.Letter == '*')
        {
            return VariantConsequence.Unknown;
        }

        if (newAa == null)
        {
            return VariantConsequence.Unknown;
        }

        if (oldAa.Letter == newAa.Letter)
        {
            return VariantConsequence.Synonymous;
        }

        return VariantConsequence.Missense;
    }

    /// <summary>
    /// Classifies consequence and calculates FrameshiftOffset when applicable.
    /// Returns (consequence, frameshiftOffset) tuple.
    /// </summary>
    private static (VariantConsequence, int?) ClassifyConsequenceWithOffset(
        AminoAcid oldAa,
        AminoAcid? newAa,
        int nucleotideDelta,
        Sequence transcript,
        CodonChange codonChange,
        int pos,
        AnnotationContext? context)
    {
        var consequence = ClassifyConsequence(oldAa, newAa, nucleotideDelta, transcript, pos, context);

        var frameshiftOffset = consequence == VariantConsequence.Frameshift
            ? CalculateFrameshiftOffset(oldAa.Letter, transcript, codonChange, pos)
            : (int?)null;

        return (consequence, frameshiftOffset);
    }

    /// <summary>
    /// Calculates the number of amino acids from the frameshift position to the new stop codon.
    /// Uses the mutated sequence (ref before pos + altSeq + ref after pos) so the
    /// frameshift reading frame is correct.
    /// </summary>
    private static int CalculateFrameshiftOffset(char startAa, Sequence transcript, CodonChange codonChange, int pos)
    {
        var refSeqStr = new string(transcript.GetData().ToArray());
        // Build the mutated sequence: ref up to pos, then alt, then ref from pos onward
        var refStart = pos - 1; // 0-based
        var afterStart = refStart + codonChange.OriginalCodon.Length;
        var after = afterStart < refSeqStr.Length
            ? refSeqStr[afterStart..]
            : "";
        var mutatedSeq = refSeqStr[..refStart] + codonChange.MutatedCodon + after;
        return CountAminosUntilStop(startAa, mutatedSeq, pos);
    }

    private static string BuildProteinHgvs(
        VariantConsequence consequence,
        AminoAcid oldAa,
        AminoAcid? newAa,
        int cPos,
        CodonChange? codonChange)
    {
        var aaIndex = (cPos - 1) / 3 + 1;

        return consequence switch
        {
            VariantConsequence.Synonymous => "p." + oldAa.Letter + aaIndex + "=",
            VariantConsequence.Missense =>
                "p." + oldAa.Letter + aaIndex + (newAa != null ? newAa.Letter.ToString() : "?"),
            VariantConsequence.Nonsense =>
                "p." + oldAa.Letter + aaIndex + "*",
            VariantConsequence.StopRetained =>
                "p." + oldAa.Letter + aaIndex + "*",
            VariantConsequence.Frameshift =>
                "p." + oldAa.Letter + aaIndex
              + (newAa != null ? newAa.Letter.ToString() : "?")
              + "fs*",
            VariantConsequence.InframeDeletion =>
                "p." + oldAa.Letter + aaIndex + "del",
            VariantConsequence.InframeInsertion =>
                "p." + oldAa.Letter + aaIndex
              + "ins" + (newAa != null ? newAa.Letter.ToString() : "?"),
            VariantConsequence.Uncertain => "p.?",
            _ => "p.?"
        };
    }

    private static string BuildHgvsCoding(int cPos, CodonChange codonChange, char? refBase, char? altBase)
    {
        if (codonChange == null)
        {
            return "c." + cPos + "?";
        }

        var refSeq = refBase != null
            ? refBase.Value.ToString().ToUpper()
            : codonChange.OriginalCodon.ToUpper();
        var altSeq = altBase != null
            ? altBase.Value.ToString().ToUpper()
            : codonChange.MutatedCodon.ToUpper();

        if (codonChange.NucleotideDelta == 0)
        {
            if (refSeq.Length == 1)
            {
                return "c." + cPos + refSeq + ">" + altSeq;
            }

            return "c." + cPos + "_" + (cPos + refSeq.Length - 1) + refSeq + ">" + altSeq;
        }

        if (codonChange.NucleotideDelta < 0)
        {
            var delLen = -codonChange.NucleotideDelta;
            if (delLen == 1)
            {
                return "c." + cPos + "del";
            }

            return "c." + cPos + "_" + (cPos + delLen - 1) + "del" + refSeq[..delLen];
        }

        var insSeq = altSeq.Length > refSeq.Length
            ? altSeq[refSeq.Length..]
            : altSeq;
        return "c." + cPos + "_" + cPos + "ins" + insSeq;
    }

    private static int CountAminosUntilStop(char startAa, string transcriptDna, int dnaStartPos)
    {
        var remaining = Math.Min(600, transcriptDna.Length - dnaStartPos + 1);
        if (remaining < 3)
        {
            return -1;
        }

        var rnaStr = CodonToRna(transcriptDna.Substring(dnaStartPos - 1, remaining));
        var count = 0;
        for (var i = 0; i + 2 < rnaStr.Length; i += 3)
        {
            var codon = rnaStr.Substring(i, 3);
            var aa = TryTranslate(codon);
            if (aa is { Letter: '*' })
            {
                return count;
            }

            count++;
            if (count > 200)
            {
                break;
            }
        }

        return -1;
    }

    /// <summary>
    /// Build a CodonChange for a single-nucleotide substitution.
    /// </summary>
    public static CodonChange? Substitution(
        string refCodon,
        int cPos,
        char refBase,
        char altBase)
    {
        if (string.IsNullOrEmpty(refCodon) || refCodon.Length != 3)
        {
            throw new ArgumentException("refCodon must be exactly 3 characters.", nameof(refCodon));
        }

        var baseInCodon = (cPos - 1) % 3; // 0, 1, or 2
        var rCodon = refCodon.ToUpper();
        var refUpper = char.ToUpper(refBase);
        var altUpper = char.ToUpper(altBase);

        if (rCodon[baseInCodon] != refUpper)
        {
            return null;
        }

        var newCodon = rCodon[..baseInCodon]
          + altUpper
          + rCodon[(baseInCodon + 1)..];
        return new CodonChange { OriginalCodon = rCodon, MutatedCodon = newCodon };
    }

    /// <summary>
    /// Build a CodonChange for a single-base deletion within a known codon.
    /// </summary>
    public static CodonChange? Deletion(string refCodon, int cPos, char delBase)
    {
        if (string.IsNullOrEmpty(refCodon) || refCodon.Length != 3)
        {
            throw new ArgumentException("refCodon must be exactly 3 characters.", nameof(refCodon));
        }

        var baseInCodon = (cPos - 1) % 3;
        var rCodon = refCodon.ToUpper();
        if (rCodon[baseInCodon] != char.ToUpper(delBase))
        {
            return null;
        }

        var newCodon = rCodon[..baseInCodon]
          + rCodon[(baseInCodon + 1)..];
        return new CodonChange { OriginalCodon = rCodon, MutatedCodon = newCodon };
    }

    /// <summary>
    /// Build a CodonChange for a single-base insertion within a known codon.
    /// </summary>
    public static CodonChange? Insertion(string refCodon, int cPos, char insBase)
    {
        if (string.IsNullOrEmpty(refCodon) || refCodon.Length != 3)
        {
            throw new ArgumentException("refCodon must be exactly 3 characters.", nameof(refCodon));
        }

        var baseInCodon = (cPos - 1) % 3;
        var rCodon = refCodon.ToUpper();
        var newCodon = rCodon[..baseInCodon]
          + char.ToUpper(insBase)
          + rCodon[baseInCodon..];
        return new CodonChange { OriginalCodon = rCodon, MutatedCodon = newCodon };
    }

    /// <summary>
    /// Build a CodonChange for a multi-base deletion (1-3 bp).
    /// </summary>
    public static CodonChange? MultiDeletion(string refCodon, int cPos, int basesToDelete)
    {
        if (string.IsNullOrEmpty(refCodon) || refCodon.Length != 3)
        {
            throw new ArgumentException("refCodon must be exactly 3 characters.", nameof(refCodon));
        }

        if (basesToDelete < 1 || basesToDelete > 3)
        {
            throw new ArgumentException("basesToDelete must be 1-3.", nameof(basesToDelete));
        }

        var baseInCodon = (cPos - 1) % 3;
        if (baseInCodon + basesToDelete > 3)
        {
            return null;
        }

        var rCodon = refCodon.ToUpper();
        var newCodon = rCodon[..baseInCodon]
          + rCodon[(baseInCodon + basesToDelete)..];
        return new CodonChange { OriginalCodon = rCodon, MutatedCodon = newCodon };
    }

    /// <summary>
    /// Convert a DNA codon (ACGT) to RNA (ACGU) for translation.
    /// </summary>
    public static string CodonToRna(string dnaCodon)
    {
        if (dnaCodon == null)
        {
            throw new ArgumentNullException(nameof(dnaCodon));
        }

        return new string(dnaCodon.ToUpper().Select(c => c == 'T' ? 'U' : c).ToArray());
    }

    /// <summary>
    /// Translate an RNA codon (3 bases) to an AminoAcid. Returns null for invalid codons.
    /// </summary>
    public static AminoAcid? TryTranslate(string rnaCodon)
    {
        if (rnaCodon == null || rnaCodon.Length != 3)
        {
            return null;
        }

        var codon = rnaCodon.ToUpper();
        return CodonTable.TryGetValue(codon, out var aa) ? aa : (AminoAcid?)null;
    }

    /// <summary>
    /// Get all RNA codons that translate to a given amino acid letter.
    /// </summary>
    public static IEnumerable<string> GetSynonymousCodons(char aa)
    {
        var targetLetter = aa;
        return CodonTable
            .Where(kv => kv.Value.Letter == targetLetter)
            .Select(kv => kv.Key);
    }

    /// <summary>
    /// Build a CodonChange for a multi-base indel spanning multiple codons.
    /// Only the affected region (from cPos covering refSubset bases, with altSeq replacing it)
    /// is returned as the mutated codon -- not the entire transcript.
    /// </summary>
    /// <param name="refSeq">Reference sequence containing at least refSubset.</param>
    /// <param name="cPos">1-based position of the first affected nucleotide.</param>
    /// <param name="refSubset">The reference bases at the variant position.</param>
    /// <param name="altSeq">The alternate bases inserted in place of refSubset.</param>
    /// <returns>A CodonChange representing the affected region, or null if validation fails.</returns>
    public static CodonChange? MultiCodonIndel(
        string refSeq,
        int cPos,
        string refSubset,
        string altSeq)
    {
        if (string.IsNullOrEmpty(refSeq))
        {
            throw new ArgumentException("Reference sequence must be provided.", nameof(refSeq));
        }

        if (string.IsNullOrEmpty(refSubset) && string.IsNullOrEmpty(altSeq))
        {
            return null;
        }

        if (cPos < 1)
        {
            return null;
        }

        var refStart = cPos - 1; // 0-based
        var refEnd = refStart + refSubset.Length;

        if (refEnd > refSeq.Length)
        {
            return null; // extends beyond sequence
        }

        var originalCodons = refSeq.Substring(refStart, refSubset.Length);
        var mutatedSubset = refSeq.Substring(refStart, refSubset.Length).Replace(refSubset, altSeq);
        return new CodonChange
        {
            OriginalCodon = originalCodons,
            MutatedCodon = mutatedSubset
        };
    }

    /// <summary>
    /// Build a CodonChange for a multi-nucleotide polymorphism (MNP).
    /// Multiple base substitutions at specified positions within codon(s).
    /// </summary>
    /// <param name="refCodons">Reference codon string(s) containing the variant positions.</param>
    /// <param name="positions">1-based positions of each substituted base within refCodons.</param>
    /// <param name="altBases">Alternate bases at those positions (one per position).</param>
    /// <returns>A CodonChange with original and mutated codon strings.</returns>
    public static CodonChange? Mnp(
        string refCodons,
        IReadOnlyList<int> positions,
        IReadOnlyList<char> altBases)
    {
        if (string.IsNullOrEmpty(refCodons))
        {
            throw new ArgumentException("Reference codons must be provided.", nameof(refCodons));
        }

        if (positions == null)
        {
            throw new ArgumentNullException(nameof(positions));
        }

        if (altBases == null)
        {
            throw new ArgumentNullException(nameof(altBases));
        }

        if (positions.Count != altBases.Count)
        {
            throw new ArgumentException("positions and altBases must have the same count.", nameof(positions));
        }

        var refUpper = refCodons.ToUpper();
        var charArray = refUpper.ToCharArray();

        for (var i = 0; i < positions.Count; i++)
        {
            var pos = positions[i];
            if (pos < 1 || pos > refUpper.Length)
            {
                return null; // position out of range
            }

            charArray[pos - 1] = char.ToUpper(altBases[i]);
        }

        return new CodonChange
        {
            OriginalCodon = refUpper,
            MutatedCodon = new string(charArray)
        };
    }

    /// <summary>
    /// Build a CodonChange for a compound deletion-insertion event.
    /// </summary>
    /// <param name="refCodon">Reference codon string (at least basesToDelete characters from cPos).</param>
    /// <param name="cPos">1-based start position of the deletion.</param>
    /// <param name="basesToDelete">Number of bases to delete starting at cPos.</param>
    /// <param name="insertionBases">Bases to insert in place of the deleted bases.</param>
    /// <returns>A CodonChange representing the delins event. Returns null if the bounds are exceeded.</returns>
    public static CodonChange? Delins(
        string refCodon,
        int cPos,
        int basesToDelete,
        string insertionBases)
    {
        if (string.IsNullOrEmpty(refCodon))
        {
            throw new ArgumentException("Reference codon must be provided.", nameof(refCodon));
        }

        if (cPos < 1)
        {
            throw new ArgumentException("Position must be >= 1.", nameof(cPos));
        }

        if (basesToDelete < 1)
        {
            throw new ArgumentException("basesToDelete must be >= 1.", nameof(basesToDelete));
        }

        var refUpper = refCodon.ToUpper();
        var refStart = cPos - 1; // 0-based

        if (refStart + basesToDelete > refUpper.Length)
        {
            return null; // deletion extends beyond codon
        }

        var originalSubset = refUpper.Substring(refStart, basesToDelete);
        var mutated = refUpper[..refStart] + insertionBases.ToUpper() +
            refUpper[(refStart + basesToDelete)..];

        return new CodonChange
        {
            OriginalCodon = originalSubset,
            MutatedCodon = mutated
        };
    }

    /// <summary>
    /// Checks whether the VCF ref allele mismatches the transcript reference at the
    /// given 1-based position. This is used in Annotate() to detect reference-allele
    /// discrepancies where the VCF record's REF doesn't agree with the transcript.
    /// Returns true when a mismatch is detected, false otherwise.
    /// Only flags true mismatches when the transcript position contains a real
    /// nucleotide base (A, C, G, or T); placeholder characters (e.g., '.') are
    /// treated as non-ambiguous and the check is skipped.
    /// </summary>
    private static bool TranscriptRefMismatch(
        char expectedRef,
        Sequence transcript,
        int position,
        CodonChange codonChange)
    {
        if (transcript == null || position < 1)
        {
            return false;
        }

        var data = transcript.GetData();
        if (position > data.Length)
        {
            return false; // position out of transcript range – skip mismatch check
        }

        var transcriptRef = char.ToUpper(data.Span[position - 1]);

        // Skip the check if the transcript position contains a placeholder
        // character (e.g., '.' from synthetic test data) rather than a real base.
        if (transcriptRef != 'A' && transcriptRef != 'C' && transcriptRef != 'G' && transcriptRef != 'T')
        {
            return false;
        }

        return transcriptRef != char.ToUpper(expectedRef);
    }
}
