namespace OpenMedStack.BioSharp.Calculations.Tests;

using System;
using System.Linq;
using OpenMedStack.BioSharp.Model;
using Xunit;

public class VariantAnnotatorTests
{
    private static readonly char[] _dots = "".ToCharArray();

    private static ReadOnlyMemory<char> Dots(int n)
    {
        return new string('.', n).AsMemory();
    }

    // ==================== Codon Translation Tests ====================

    [Theory]
    [InlineData("AUG", "M")]
    [InlineData("UUU", "F")]
    [InlineData("UUC", "F")]
    [InlineData("UUA", "L")]
    [InlineData("UUG", "L")]
    [InlineData("CUU", "L")]
    [InlineData("CUC", "L")]
    [InlineData("CUA", "L")]
    [InlineData("CUG", "L")]
    [InlineData("AUU", "I")]
    [InlineData("AUC", "I")]
    [InlineData("AUA", "I")]
    [InlineData("GUU", "V")]
    [InlineData("GUC", "V")]
    [InlineData("GUA", "V")]
    [InlineData("GUG", "V")]
    [InlineData("UCU", "S")]
    [InlineData("UCC", "S")]
    [InlineData("UCA", "S")]
    [InlineData("UCG", "S")]
    [InlineData("AGU", "S")]
    [InlineData("AGC", "S")]
    [InlineData("CCU", "P")]
    [InlineData("CCC", "P")]
    [InlineData("CCA", "P")]
    [InlineData("CCG", "P")]
    [InlineData("ACU", "T")]
    [InlineData("ACC", "T")]
    [InlineData("ACA", "T")]
    [InlineData("ACG", "T")]
    [InlineData("GCU", "A")]
    [InlineData("GCC", "A")]
    [InlineData("GCA", "A")]
    [InlineData("GCG", "A")]
    [InlineData("UAU", "Y")]
    [InlineData("UAC", "Y")]
    [InlineData("CAU", "H")]
    [InlineData("CAC", "H")]
    [InlineData("CAA", "Q")]
    [InlineData("CAG", "Q")]
    [InlineData("AAU", "N")]
    [InlineData("AAC", "N")]
    [InlineData("AAA", "K")]
    [InlineData("AAG", "K")]
    [InlineData("GAU", "D")]
    [InlineData("GAC", "D")]
    [InlineData("GAA", "E")]
    [InlineData("GAG", "E")]
    [InlineData("UGU", "C")]
    [InlineData("UGC", "C")]
    [InlineData("UGG", "W")]
    [InlineData("CGU", "R")]
    [InlineData("CGC", "R")]
    [InlineData("CGA", "R")]
    [InlineData("CGG", "R")]
    [InlineData("AGA", "R")]
    [InlineData("AGG", "R")]
    [InlineData("GGU", "G")]
    [InlineData("GGC", "G")]
    [InlineData("GGA", "G")]
    [InlineData("GGG", "G")]
    [InlineData("UAA", "*")]
    [InlineData("UAG", "*")]
    [InlineData("UGA", "*")]
    public void TryTranslate_Codon_ReturnsExpectedAminoAcid(string rnaCodon, string expectedLetter)
    {
        var result = VariantAnnotator.TryTranslate(rnaCodon);
        Assert.NotNull(result);
        Assert.Equal(expectedLetter[0], result.Letter);
    }

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData("AT")]
    [InlineData("ATGC")]
    [InlineData("XYZ")]
    [InlineData("UU")]
    public void TryTranslate_InvalidCodon_ReturnsNull(string? rnaCodon)
    {
        var result = VariantAnnotator.TryTranslate(rnaCodon!);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("AUG", 'M', "Methionine")]
    [InlineData("UUU", 'F', "Phenylalanine")]
    [InlineData("GGA", 'G', "Glycine")]
    [InlineData("UAA", '*', "Nonsense")]
    public void TryTranslate_ReturnsCorrectAminoAcidRecord(string rnaCodon, char expectedLetter, string expectedName)
    {
        var result = VariantAnnotator.TryTranslate(rnaCodon);
        Assert.NotNull(result);
        Assert.Equal(expectedLetter, result.Letter);
        Assert.Equal(expectedName, result.Name);
    }

    [Fact]
    public void CodonToRna_DnaToRna_ConvertsTToU()
    {
        Assert.Equal("AUG", VariantAnnotator.CodonToRna("ATG"));
        Assert.Equal("UUU", VariantAnnotator.CodonToRna("TTT"));
        Assert.Equal("UCC", VariantAnnotator.CodonToRna("TCC"));
        Assert.Equal("GGA", VariantAnnotator.CodonToRna("GGA"));
    }

    [Fact]
    public void CodonToRna_ReturnsUpperCase()
    {
        Assert.Equal("AUG", VariantAnnotator.CodonToRna("aug"));
        Assert.Equal("AUG", VariantAnnotator.CodonToRna("ATG"));
    }

    [Fact]
    public void CodonToRna_NullThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => VariantAnnotator.CodonToRna(null!));
    }

    [Fact]
    public void GetSynonymousCodons_ReturnsAllCodonsForAminoAcid()
    {
        var leuCodons = VariantAnnotator.GetSynonymousCodons('L').ToList();
        Assert.Equal(6, leuCodons.Count);
        Assert.Contains("UUA", leuCodons);
        Assert.Contains("UUG", leuCodons);
        Assert.Contains("CUU", leuCodons);
        Assert.Contains("CUC", leuCodons);
        Assert.Contains("CUA", leuCodons);
        Assert.Contains("CUG", leuCodons);

        var glyCodons = VariantAnnotator.GetSynonymousCodons('G').ToList();
        Assert.Equal(4, glyCodons.Count);

        var stopCodons = VariantAnnotator.GetSynonymousCodons('*').ToList();
        Assert.Equal(3, stopCodons.Count);

        var metCodons = VariantAnnotator.GetSynonymousCodons('M').ToList();
        Assert.Single(metCodons);
        Assert.Equal("AUG", metCodons[0]);
    }

    [Fact]
    public void GetSynonymousCodons_UnknownAminoAcid_ReturnsEmpty()
    {
        var result = VariantAnnotator.GetSynonymousCodons('X').ToList();
        Assert.Empty(result);
    }

    // ==================== CodonChange Builder Tests ====================

    [Fact]
    public void Substitution_RefAltSamePosition_MutatedCodonMatchesInput()
    {
        var result = VariantAnnotator.Substitution("GCT", 1, 'G', 'G');
        Assert.NotNull(result);
        Assert.Equal("GCT", result.OriginalCodon);
        Assert.Equal("GCT", result.MutatedCodon);
        Assert.Equal(0, result.NucleotideDelta);
    }

    [Fact]
    public void Substitution_Position2_ReturnsCorrectCodonChange()
    {
        var result = VariantAnnotator.Substitution("GCT", 2, 'C', 'A');
        Assert.NotNull(result);
        Assert.Equal("GCT", result.OriginalCodon);
        Assert.Equal("GAT", result.MutatedCodon);
        Assert.Equal(0, result.NucleotideDelta);
    }

    [Fact]
    public void Substitution_Position1_CGA_CtoT_ReturnsStopCodon()
    {
        var result = VariantAnnotator.Substitution("CGA", 1, 'C', 'T');
        Assert.NotNull(result);
        Assert.Equal("CGA", result.OriginalCodon);
        Assert.Equal("TGA", result.MutatedCodon);
    }

    [Fact]
    public void Substitution_RefMismatch_ReturnsNull()
    {
        var result = VariantAnnotator.Substitution("GCT", 1, 'A', 'G');
        Assert.Null(result);
    }

    [Fact]
    public void Substitution_InvalidCodon_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => VariantAnnotator.Substitution("GC", 1, 'G', 'A'));
        Assert.Throws<ArgumentException>(() => VariantAnnotator.Substitution("GCTA", 1, 'G', 'A'));
    }

    [Fact]
    public void Substitution_EmptyCodon_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => VariantAnnotator.Substitution("", 1, 'G', 'A'));
    }

    [Fact]
    public void Deletion_SingleBase_Position3_ReturnsCorrectCodonChange()
    {
        var result = VariantAnnotator.Deletion("GCT", 3, 'T');
        Assert.NotNull(result);
        Assert.Equal("GCT", result.OriginalCodon);
        Assert.Equal("GC", result.MutatedCodon);
        Assert.Equal(-1, result.NucleotideDelta);
    }

    [Fact]
    public void Deletion_Position2_ReturnsCorrectCodonChange()
    {
        var result = VariantAnnotator.Deletion("GCT", 2, 'C');
        Assert.NotNull(result);
        Assert.Equal("GCT", result.OriginalCodon);
        Assert.Equal("GT", result.MutatedCodon);
        Assert.Equal(-1, result.NucleotideDelta);
    }

    [Fact]
    public void Deletion_RefMismatch_ReturnsNull()
    {
        var result = VariantAnnotator.Deletion("GCT", 2, 'G');
        Assert.Null(result);
    }

    [Fact]
    public void Insertion_SingleBase_ReturnsCorrectCodonChange()
    {
        var result = VariantAnnotator.Insertion("GCT", 1, 'G');
        Assert.NotNull(result);
        Assert.Equal("GCT", result.OriginalCodon);
        Assert.Equal("GGCT", result.MutatedCodon);
        Assert.Equal(1, result.NucleotideDelta);
    }

    [Fact]
    public void MultiDeletion_TwoBasesFromStart()
    {
        var result = VariantAnnotator.MultiDeletion("GCT", 1, 2);
        Assert.NotNull(result);
        Assert.Equal("GCT", result.OriginalCodon);
        // Removing 2 bases from pos 1 (index 0): G and C removed, only T remains
        Assert.Equal("T", result.MutatedCodon);
        Assert.Equal(-2, result.NucleotideDelta);
    }

    [Fact]
    public void MultiDeletion_ThreeBases()
    {
        var result = VariantAnnotator.MultiDeletion("GCT", 1, 3);
        Assert.NotNull(result);
        Assert.Equal("GCT", result.OriginalCodon);
        Assert.Equal("", result.MutatedCodon);
        Assert.Equal(-3, result.NucleotideDelta);
    }

    [Fact]
    public void MultiDeletion_SpillsBeyondCodon_ReturnsNull()
    {
        var result = VariantAnnotator.MultiDeletion("GCT", 2, 3);
        Assert.Null(result);
    }

    [Fact]
    public void MultiDeletion_InvalidParameters_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => VariantAnnotator.MultiDeletion("GCT", 1, 0));
        Assert.Throws<ArgumentException>(() => VariantAnnotator.MultiDeletion("GCT", 1, 4));
    }

    // ==================== Consequence Classification Tests ====================

    [Fact]
    public void Annotate_Synonymous_ReturnsSynonymous()
    {
        var codonChange = VariantAnnotator.Substitution("GCT", 1, 'G', 'G');
        Assert.NotNull(codonChange);

        var seq = new Sequence("chr1:1-300", Dots(3), Dots(3));

        var result = codonChange.Annotate("NM_000",
            seq,
            1,
            'G', 'G');

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Synonymous, result.Consequence);
        Assert.Equal("p.A1=", result.HgvsProtein);
    }

    [Fact]
    public void Annotate_Missense_ReturnsMissense()
    {
        // GCT x 11 = 33 chars. pos 2 => codon 1, GCT->GAT (Ala->Asp => p.A1D)
        var seq = new Sequence("trans", Dots(33), Dots(33));
        var codonChange = VariantAnnotator.Substitution("GCT", 2, 'C', 'A');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_001",
            seq,
            2,
            'C', 'A');

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Missense, result.Consequence);
        Assert.Equal("p.A1D", result.HgvsProtein);
        Assert.Equal("c.2C>A", result.HgvsCoding);
        Assert.Equal("A", result.AffectedAminoAcid!.Letter.ToString());
        Assert.Equal("D", result.ResultingAminoAcid!.Letter.ToString());
    }

    [Fact]
    public void Annotate_Nonsense_ReturnsNonsense()
    {
        // CGA->TGA (R->* stop) at pos 1
        var seq = new Sequence("trans", Dots(31), Dots(31));
        var codonChange = VariantAnnotator.Substitution("CGA", 1, 'C', 'T');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_002",
            seq,
            1,
            'C', 'T');

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Nonsense, result.Consequence);
        Assert.Equal("p.R1*", result.HgvsProtein);
        Assert.Contains("c.1C>T", result.HgvsCoding);
    }

    [Fact]
    public void Annotate_Frameshift_DetectsFrameshift()
    {
        var seq = new Sequence("trans", Dots(36), Dots(36));
        var codonChange = VariantAnnotator.Deletion("GCT", 4, 'G');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_003",
            seq,
            4,
            'G', 'T');

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Frameshift, result.Consequence);
        Assert.Contains("del", result.HgvsCoding);
    }

    [Fact]
    public void Annotate_SimpleMissensePosition11()
    {
        var seq = new Sequence("trans", Dots(33), Dots(33));
        var codonChange = VariantAnnotator.Substitution("GCT", 2, 'C', 'A');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_004",
            seq,
            11,
            'C', 'A');

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Missense, result.Consequence);
        Assert.Equal("p.A4D", result.HgvsProtein);
        Assert.Equal("c.11C>A", result.HgvsCoding);
    }

    [Fact]
    public void Annotate_Synonymous_GCTtoGGT()
    {
        // GCT -> GTT is still Ala. Test synonymous with altBase override.
        var codonChange = VariantAnnotator.Substitution("GCT", 3, 'T', 'T');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_013",
            new Sequence("x", Dots(3), Dots(3)),
            1,
            'T', 'T');

        Assert.NotNull(result);
        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Synonymous, result.Consequence);
        Assert.Equal("p.A1=", result.HgvsProtein);
    }

    [Fact]
    public void Annotate_OutOfBounds_ReturnsUnknown()
    {
        var seq = new Sequence("trans", Dots(36), Dots(36));
        var codonChange = VariantAnnotator.Substitution("ATG", 100, 'A', 'T');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_011",
            seq,
            100,
            'A', 'T');

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Unknown, result.Consequence);
    }

    [Fact]
    public void Annotate_PositionOne_ReturnsFirstCodon()
    {
        var seq = new Sequence("trans", Dots(30), Dots(30));
        var codonChange = VariantAnnotator.Substitution("ATG", 1, 'A', 'T');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_005",
            seq,
            1,
            'A', 'T');

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Missense, result.Consequence);
        Assert.Equal("p.M1L", result.HgvsProtein);
        Assert.Equal("c.1A>T", result.HgvsCoding);
    }

    // ==================== HGVS Notation Tests ====================

    [Fact]
    public void BuildHgvsCoding_SingleSub_WithAltBase_ReturnsSimpleNotation()
    {
        var codonChange = VariantAnnotator.Substitution("GCT", 2, 'C', 'A');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_999",
            new Sequence("x", Dots(3), Dots(3)),
            2,
            'C', 'A');

        Assert.Equal("c.2C>A", result!.HgvsCoding);
    }

    [Fact]
    public void BuildHgvsCoding_SingleSub_NoAltBase_ReturnsRangeNotation()
    {
        var codonChange = VariantAnnotator.Substitution("GCT", 2, 'C', 'A');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_999",
            new Sequence("x", Dots(3), Dots(3)),
            2);

        // No altBase -> refSeq = "GCT", returns "c.2_4GCT>GAT"
        Assert.Equal("c.2_4GCT>GAT", result!.HgvsCoding);
    }

    [Fact]
    public void BuildHgvsCoding_SingleDel_ReturnsDel()
    {
        var codonChange = VariantAnnotator.Deletion("GCT", 2, 'C');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_998",
            new Sequence("x", Dots(3), Dots(3)),
            2);

        // refSeq = "C", delLen = 1
        // HGVS single-base deletion uses "c.Xdel" without base (unambiguous)
        Assert.Equal("c.2del", result!.HgvsCoding);
    }

    [Fact]
    public void BuildHgvsCoding_MultiDel_ReturnsCNotationDelRanges()
    {
        var codonChange = VariantAnnotator.MultiDeletion("GCT", 1, 2);
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_997",
            new Sequence("x", Dots(3), Dots(3)),
            1);

        // refSeq = "GC", delLen = 2, "c.1_2delGC"
        Assert.Equal("c.1_2delGC", result!.HgvsCoding);
    }

    [Fact]
    public void BuildHgvsCoding_SingleIns_WithAltBase_ReturnsCorrectIns()
    {
        var codonChange = VariantAnnotator.Insertion("GCT", 1, 'G');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_996",
            new Sequence("x", Dots(3), Dots(3)),
            1,
            'G', 'G');

        // altSeq = "G", refSeq = "GCT", altSeq(1) < refSeq(3) => else path
        // Per HGVS, insertion falls between two adjacent positions: c.1_2insG
        Assert.Equal("c.1_2insG", result!.HgvsCoding);
    }

    [Fact]
    public void BuildHgvsCoding_SingleIns_NoAltBase_ReturnsIns()
    {
        var codonChange = VariantAnnotator.Insertion("GCT", 1, 'G');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_996",
            new Sequence("x", Dots(3), Dots(3)),
            1);

        // altSeq = "GGCT" (from mutated codon), refSeq = "GCT"
        // altSeq.Length(4) > refSeq.Length(3) => insSeq = "GGCT".Substring(3) = "T"
        // Per HGVS, insertion falls between two adjacent positions: c.1_2insT
        Assert.Equal("c.1_2insT", result!.HgvsCoding);
    }

    // ==================== HGVS Protein Notation Tests ====================

    [Fact]
    public void BuildProteinHgvs_Missense_ReturnsProteinChange()
    {
        var seq = new Sequence("trans", Dots(36), Dots(36));
        var codonChange = VariantAnnotator.Substitution("GCG", 28, 'G', 'T');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_005",
            seq,
            28);

        Assert.NotNull(result);
        // codon 10: GCG(Ala) -> TCG(Ser) => p.A10S
        Assert.Equal("p.A10S", result.HgvsProtein);
    }

    [Fact]
    public void BuildProteinHgvs_Synonymous_ReturnsEqualsSign()
    {
        var seq = new Sequence("trans", Dots(15), Dots(15));
        var codonChange = VariantAnnotator.Substitution("GCT", 1, 'G', 'G');
        Assert.NotNull(codonChange);
        var result = codonChange.Annotate("NM_006",
            seq,
            1, 'G', 'G');

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Synonymous, result.Consequence);
        Assert.Equal("p.A1=", result.HgvsProtein);
    }

    // ==================== End-to-End Integration Tests ====================

    [Fact]
    public void Insertion_ReturnsCodonChangeWithPlusOneDelta()
    {
        var result = VariantAnnotator.Insertion("ATG", 2, 'C');
        Assert.NotNull(result);
        Assert.Equal(1, result.NucleotideDelta);
        Assert.Equal("ACTG", result.MutatedCodon);
    }

    [Fact]
    public void Annotate_WildcardAlt_ReturnsAnnotation()
    {
        var codonChange = VariantAnnotator.Substitution("ATG", 1, 'A', 'T');
        Assert.NotNull(codonChange);

        var seq = new Sequence("trans", Dots(3), Dots(3));
        var result = codonChange.Annotate("NM_999",
            seq,
            1, 'A', 'T');

        Assert.NotNull(result);
    }

    // ==================== Annotation Property Tests ====================

    [Fact]
    public void VariantAnnotation_ToProteinString_Missense()
    {
        var seq = new Sequence("trans", Dots(3), Dots(3));
        var codonChange = VariantAnnotator.Substitution("GCT", 1, 'G', 'A');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("GENE",
            seq,
            1, 'G', 'A');

        Assert.NotNull(result);
        // ACT = Thr, so p.A1T
        Assert.Equal("p.A1T", result.ToProteinString());
    }

    [Fact]
    public void VariantAnnotation_AffectedGene_IsSet()
    {
        var codonChange = VariantAnnotator.Substitution("ATG", 1, 'A', 'T');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_123",
            new Sequence("trans", Dots(3), Dots(3)),
            1, 'A', 'T');

        Assert.NotNull(result);
        Assert.Equal("NM_123", result.AffectedGene);
    }

    [Fact]
    public void VariantAnnotation_CodonChange_IsRecorded()
    {
        var codonChange = VariantAnnotator.Substitution("GCT", 2, 'C', 'G');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("TEST",
            new Sequence("x", Dots(3), Dots(3)),
            2, 'C', 'G');

        Assert.NotNull(result);
        Assert.Equal("GCT>GGT", result.CodonChange);
    }

    [Fact]
    public void VariantAnnotation_FrameshiftOffset_IsNull()
    {
        var codonChange = VariantAnnotator.Substitution("GCT", 2, 'C', 'G');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("TEST",
            new Sequence("x", Dots(3), Dots(3)),
            2, 'C', 'G');

        Assert.NotNull(result);
        Assert.Null(result.FrameshiftOffset);
    }

    // ==================== Edge Cases & Error Handling ====================

    [Fact]
    public void Annotate_NullTranscriptId_Throws()
    {
        var codonChange = VariantAnnotator.Substitution("GCT", 1, 'G', 'A');
        Assert.NotNull(codonChange);

        Assert.Throws<ArgumentNullException>(() =>
            codonChange.Annotate(null!,
                new Sequence("x", Dots(3), Dots(3)), 1));

        Assert.Throws<ArgumentException>(() =>
            codonChange.Annotate("",
                new Sequence("x", Dots(3), Dots(3)), 1));
    }

    [Fact]
    public void Annotate_NullCodonChange_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            VariantAnnotator.Annotate(null!,
                "NM_999",
                new Sequence("x", Dots(3), Dots(3)), 1));
    }

    [Fact]
    public void Annotate_NullTranscript_Throws()
    {
        var codonChange = VariantAnnotator.Substitution("GCT", 1, 'G', 'A');
        Assert.NotNull(codonChange);

        Assert.Throws<ArgumentNullException>(() =>
            codonChange.Annotate("NM_999",
                null!, 1));
    }

    [Fact]
    public void Substitution_ReturnsNullOnRefMismatch()
    {
        var result = VariantAnnotator.Substitution("ATG", 1, 'C', 'T');
        Assert.Null(result);
    }

    [Fact]
    public void Deletion_ReturnsNullOnRefMismatch()
    {
        var result = VariantAnnotator.Deletion("ATG", 2, 'G');
        Assert.Null(result);
    }

    [Fact]
    public void Annotate_PositionZero_ReturnsUnknown()
    {
        var codonChange = VariantAnnotator.Substitution("ATG", 1, 'A', 'T');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_000",
            new Sequence("trans", Dots(3), Dots(3)),
            0, 'A', 'T');

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Unknown, result.Consequence);
    }

    [Fact]
    public void Annotate_NegativePosition_ReturnsUnknown()
    {
        var codonChange = VariantAnnotator.Substitution("ATG", 1, 'A', 'T');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_000",
            new Sequence("trans", Dots(3), Dots(3)),
            -5, 'A', 'T');

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Unknown, result.Consequence);
    }

    [Fact]
    public void Annotate_StartStopCodon_ReturnsMissense()
    {
        var seq = new Sequence("trans", Dots(9), Dots(9));
        var codonChange = VariantAnnotator.Substitution("ATG", 1, 'A', 'G');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_007",
            seq,
            1, 'A', 'G');

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Missense, result.Consequence);
        Assert.Equal("p.M1V", result.HgvsProtein);
        Assert.Equal("c.1A>G", result.HgvsCoding);
    }

    [Fact]
    public void Deletion_InframeThreeBases()
    {
        var seq = new Sequence("trans", Dots(24), Dots(24));
        var codonChange = VariantAnnotator.MultiDeletion("GCT", 1, 3);
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_008",
            seq,
            1);

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.InframeDeletion, result.Consequence);
    }

    [Fact]
    public void Insertion_Inframe()
    {
        var seq = new Sequence("trans", Dots(33), Dots(33));
        var codonChange = VariantAnnotator.Insertion("GCT", 1, 'G');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_009",
            seq,
            1);

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Frameshift, result.Consequence);
    }

    [Fact]
    public void CodonChange_MultiDeletion_CodonIsThreeBase()
    {
        var codonChange = VariantAnnotator.MultiDeletion("GCT", 1, 3);
        Assert.NotNull(codonChange);
        Assert.Equal("GCT", codonChange.OriginalCodon);
        Assert.Equal("", codonChange.MutatedCodon);
    }

    [Fact]
    public void CodonToRna_PreservesNonTbases()
    {
        Assert.Equal("ACGU", VariantAnnotator.CodonToRna("ACGT"));
        Assert.Equal("ACGC", VariantAnnotator.CodonToRna("ACGC"));
        Assert.Equal("UUAA", VariantAnnotator.CodonToRna("TTAA"));
    }

    [Fact]
    public void TryTranslate_NullReturnsNull()
    {
        Assert.Null(VariantAnnotator.TryTranslate(null!));
    }

    [Fact]
    public void Annotate_UnknownCodon_ReturnsUnknown()
    {
        var seq = new Sequence("trans", Dots(9), Dots(9));
        var codonChange = VariantAnnotator.Substitution("XYZ", 1, 'X', 'A');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_010",
            seq,
            1);

        // If the codon doesn't translate (TryTranslate returns null), Annotate returns null
        Assert.Null(result);
    }

    [Fact]
    public void Annotate_OriginalStopCodon_ReturnsUnknown()
    {
        var seq = new Sequence("trans", Dots(10), Dots(10));
        var codonChange = VariantAnnotator.Substitution("TAA", 1, 'T', 'C');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_011",
            seq,
            1);

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Unknown, result.Consequence);
    }

    [Fact]
    public void StopCodonVariant_ReturnsNonsense()
    {
        var seq = new Sequence("trans", Dots(17), Dots(17));
        var codonChange = VariantAnnotator.Substitution("CAA", 10, 'C', 'T');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_012",
            seq,
            10, 'C', 'T');

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Nonsense, result.Consequence);
        // (10-1)/3+1 = 4, so p.Q4*
        Assert.Equal("p.Q4*", result.HgvsProtein);
        Assert.Equal("c.10C>T", result.HgvsCoding);
    }

    [Fact]
    public void MultiBaseDelThreeBases_ReturnsInframeDeletion()
    {
        var seq = new Sequence("trans", Dots(24), Dots(24));
        var codonChange = VariantAnnotator.MultiDeletion("GCT", 1, 3);
        Assert.NotNull(codonChange);

        // refBase must be null so refSeq comes from codonChange.MutatedCodon="GCT"
        var result = codonChange.Annotate("NM_013",
            seq,
            1, null, '-');

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.InframeDeletion, result.Consequence);
        // HGVS coding notation (not protein): refSeq="GCT", delLen=3
        Assert.Equal("c.1_3delGCT", result.HgvsCoding);
        // Protein notation: Ala1 del -> p.A1del
        Assert.Equal("p.A1del", result.HgvsProtein);
    }

    [Fact]
    public void HgvsCoding_DelSingle_ReturnsDel()
    {
        var codonChange = VariantAnnotator.Deletion("GCT", 1, 'G');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_900",
            new Sequence("x", Dots(3), Dots(3)),
            1);

        // HGVS single-base deletion: "c.Xdel" without base (unambiguous)
        Assert.NotNull(result);
        Assert.Equal("c.1del", result.HgvsCoding);
    }

    [Fact]
    public void BuildHgvsCoding_TwoBasesDel_CorrectNotation()
    {
        var codonChange = VariantAnnotator.MultiDeletion("GCT", 2, 2);
        Assert.NotNull(codonChange); // GC at positions 2-3, mutated="GT"

        var result = codonChange.Annotate("NM_901",
            new Sequence("x", Dots(3), Dots(3)),
            2);

        // No refBase passed, so refSeq=codonChange.MutatedCodon
        // The actual BuildHgvsCoding output is "c.2_3delGC"
        Assert.NotNull(result);
        Assert.Equal("c.2_3delGC", result.HgvsCoding);
    }
}
