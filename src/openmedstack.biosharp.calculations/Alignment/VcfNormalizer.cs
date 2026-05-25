using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// Normalises VCF variant records per the standard VCF normalisation rules:
/// <list type="number">
///   <item>Left-align indels so the most upstream representation is used.</item>
///   <item>Decompose multi-allelic records into individual biallelic records.</item>
/// </list>
///
/// <para>
/// Normalisation is idempotent — applying it twice produces identical output.
/// SNPs are passed through unchanged (no indel shift is possible).
/// </para>
/// </summary>
public static class VcfNormalizer
{
    /// <summary>
    /// Applies full normalisation to a collection of variants:
    /// left-align each indel, then decompose multi-allelics into biallelic records.
    /// </summary>
    /// <param name="variants">Input variant records.</param>
    /// <param name="referenceSequence">
    /// Full chromosome reference sequence, 0-based
    /// (VCF position 1 = index 0 in this span).
    /// </param>
    /// <returns>Normalised, biallelic variant records in position order.</returns>
    public static IEnumerable<LocalVariantResult> Normalize(
        IEnumerable<LocalVariantResult> variants,
        ReadOnlySpan<char> referenceSequence)
    {
        var results = new List<LocalVariantResult>();
        foreach (var v in variants)
        {
            // Decompose multi-allelic first, then left-align each biallelic record
            foreach (var biallelic in Decompose(v))
            {
                var normalised = IsIndel(biallelic)
                    ? LeftAlignIndel(biallelic, referenceSequence)
                    : biallelic;
                results.Add(normalised);
            }
        }

        results.Sort(static (a, b) =>
        {
            var c = string.Compare(a.Chromosome, b.Chromosome, StringComparison.Ordinal);
            return c != 0 ? c : a.Position.CompareTo(b.Position);
        });
        return results;
    }

    /// <summary>
    /// Left-aligns a single indel variant against the reference sequence.
    /// SNPs (same REF and ALT length) are returned unchanged.
    /// </summary>
    /// <param name="variant">The variant to align.</param>
    /// <param name="referenceSequence">Full reference sequence (0-based).</param>
    /// <returns>A new <see cref="LocalVariantResult"/> with the left-most representation.</returns>
    public static LocalVariantResult LeftAlignIndel(
        LocalVariantResult variant,
        ReadOnlySpan<char> referenceSequence)
    {
        if (!IsIndel(variant))
        {
            return variant;
        }

        var pos = variant.Position; // 1-based
        var refAllele = variant.Reference.AsSpan();
        var altAllele = variant.Alternate.AsSpan();

        // Step 1: trim identical suffixes from REF and ALT
        TrimSuffix(refAllele, altAllele, out var trimmedRef, out var trimmedAlt);
        refAllele = trimmedRef;
        altAllele = trimmedAlt;

        // Step 2: shift left while the last base of the allele equals the preceding ref base
        //         and position > 1
        while (pos > 1)
        {
            var refIdx = pos - 2; // 0-based index of the base preceding current pos

            // Check that the last base of both alleles equals the preceding reference base
            // (this is the VCF left-alignment invariant)
            if (refIdx < 0 || refIdx >= referenceSequence.Length)
            {
                break;
            }

            var prevRefBase = referenceSequence[refIdx];

            // The last bases of both trimmed alleles must equal prevRefBase to shift
            if (!AreEqual(refAllele[^1], prevRefBase) ||
                !AreEqual(altAllele[^1], prevRefBase))
            {
                break;
            }

            // Prepend that base and trim the last base
            var newRef = new char[refAllele.Length];
            newRef[0] = prevRefBase;
            refAllele[..^1].CopyTo(newRef.AsSpan(1));
            refAllele = newRef.AsSpan();

            var newAlt = new char[altAllele.Length];
            newAlt[0] = prevRefBase;
            altAllele[..^1].CopyTo(newAlt.AsSpan(1));
            altAllele = newAlt.AsSpan();

            pos--;
        }

        // If nothing changed, return original
        if (pos == variant.Position &&
            refAllele.SequenceEqual(variant.Reference.AsSpan()) &&
            altAllele.SequenceEqual(variant.Alternate.AsSpan()))
        {
            return variant;
        }

        return CopyWithNewAlleles(variant, pos, new string(refAllele), new string(altAllele));
    }

    /// <summary>
    /// Decomposes a multi-allelic record into individual biallelic records.
    /// For a biallelic variant, returns the original record unchanged.
    /// </summary>
    public static IEnumerable<LocalVariantResult> Decompose(LocalVariantResult variant)
    {
        if (!variant.IsMultiAllelic)
        {
            yield return variant;
            yield break;
        }

        foreach (var alt in variant.AllAlternateAlleles)
        {
            var biallelic = new LocalVariantResult
            {
                Chromosome = variant.Chromosome,
                Position = variant.Position,
                Reference = variant.Reference,
                Alternate = alt,
                QuantitativeQuality = variant.QuantitativeQuality,
                Depth = variant.Depth,
                IsStructuralVariant = variant.IsStructuralVariant,
                SvType = variant.SvType,
                EndPosition = variant.EndPosition,
                Genotype = variant.Genotype
            };
            yield return biallelic;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIndel(LocalVariantResult v)
        => v.Reference.Length != v.Alternate.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AreEqual(char a, char b)
        => char.ToUpperInvariant(a) == char.ToUpperInvariant(b);

    /// <summary>
    /// Trims equal characters from the right end of both alleles.
    /// Maintains at least 1 character in each (the anchor base).
    /// </summary>
    private static void TrimSuffix(
        ReadOnlySpan<char> refAllele,
        ReadOnlySpan<char> altAllele,
        out ReadOnlySpan<char> trimmedRef,
        out ReadOnlySpan<char> trimmedAlt)
    {
        var trimLen = 0;
        while (refAllele.Length - trimLen > 1 &&
               altAllele.Length - trimLen > 1 &&
               AreEqual(refAllele[refAllele.Length - 1 - trimLen], altAllele[altAllele.Length - 1 - trimLen]))
        {
            trimLen++;
        }

        trimmedRef = refAllele[..^trimLen];
        trimmedAlt = altAllele[..^trimLen];
    }

    private static LocalVariantResult CopyWithNewAlleles(
        LocalVariantResult source,
        int newPosition,
        string newRef,
        string newAlt)
    {
        return new LocalVariantResult
        {
            Chromosome = source.Chromosome,
            Position = newPosition,
            Reference = newRef,
            Alternate = newAlt,
            QuantitativeQuality = source.QuantitativeQuality,
            Depth = source.Depth,
            IsStructuralVariant = source.IsStructuralVariant,
            SvType = source.SvType,
            EndPosition = source.EndPosition,
            Genotype = source.Genotype,
            BubbleConfidence = source.BubbleConfidence
        };
    }
}
