namespace OpenMedStack.BioSharp.Calculations.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Model;

/// <summary>
/// Generates synthetic reference sequences, simulated reads, and variant test sets
/// for use in unit and integration tests.
/// </summary>
public sealed class TestDataGenerator
{
    private static readonly char[] Bases = ['A', 'C', 'G', 'T'];

    private readonly Random _rng;

    /// <param name="seed">Seed for reproducible output.</param>
    public TestDataGenerator(int seed = 0)
    {
        _rng = new Random(seed);
    }

    /// <summary>
    /// Generates a random DNA reference sequence.
    /// </summary>
    /// <param name="length">Number of bases.</param>
    /// <param name="repeatFraction">
    /// Fraction of the sequence that is a simple AT/GC repeat motif (0–1).
    /// </param>
    public string GenerateReference(int length, double repeatFraction = 0.0)
    {
        var buffer = new char[length];
        var repeatLength = (int)(length * repeatFraction);

        // Fill first portion with a simple ATGCATGC... repeat
        for (var i = 0; i < repeatLength; i++)
        {
            buffer[i] = (i % 4) switch
            {
                0 => 'A',
                1 => 'T',
                2 => 'G',
                _ => 'C'
            };
        }

        // Fill the rest with random bases
        for (var i = repeatLength; i < length; i++)
        {
            buffer[i] = Bases[_rng.Next(4)];
        }

        // Shuffle everything so repeats aren't always at the start
        for (var i = length - 1; i > 0; i--)
        {
            var j = _rng.Next(i + 1);
            (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
        }

        return new string(buffer);
    }

    /// <summary>
    /// Simulates reads sampled from a reference at the given depth.
    /// The number of reads produced is approximately <c>depth × length / readLength</c>.
    /// </summary>
    public async IAsyncEnumerable<Sequence> SimulateReads(
        string reference,
        int depth,
        int readLength)
    {
        var readCount = depth * reference.Length / readLength;
        for (var i = 0; i < readCount; i++)
        {
            var start = _rng.Next(Math.Max(1, reference.Length - readLength + 1));
            var end = Math.Min(start + readLength, reference.Length);
            var actualLen = end - start;

            var bases = new char[actualLen];
            var quals = new char[actualLen];
            reference.AsSpan(start, actualLen).CopyTo(bases);

            // Assign uniform high-quality scores (Phred 40 = 'I' in Phred+33)
            for (var q = 0; q < actualLen; q++)
            {
                quals[q] = 'I';
            }

            yield return new Sequence($"read_{i}", bases, quals);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a copy of <paramref name="reference"/> with the specified SNVs applied.
    /// </summary>
    public string InjectVariants(string reference, SyntheticVariant[] variants)
    {
        var buffer = reference.ToCharArray();
        foreach (var v in variants)
        {
            if (v.Position >= 0 && v.Position < buffer.Length)
            {
                buffer[v.Position] = v.AlternateAllele;
            }
        }
        return new string(buffer);
    }

    /// <summary>
    /// Generates a complete variant test set: reference, variants, and reads from the mutated reference.
    /// </summary>
    public async Task<(string reference, List<SyntheticVariant> variants, List<Sequence> reads)>
        GenerateVariantSet(int referenceLength, int variantCount, int readDepth, int readLength)
    {
        var reference = GenerateReference(referenceLength);

        // Pick unique positions distributed across the reference
        var positions = new HashSet<int>();
        while (positions.Count < variantCount)
            positions.Add(_rng.Next(referenceLength));

        var variants = new List<SyntheticVariant>(variantCount);
        foreach (var pos in positions)
        {
            var refAllele = reference[pos];
            // Pick a different base for the alternate allele
            char altAllele;
            do { altAllele = Bases[_rng.Next(4)]; } while (altAllele == refAllele);
            variants.Add(new SyntheticVariant { Position = pos, ReferenceAllele = refAllele, AlternateAllele = altAllele });
        }

        var mutated = InjectVariants(reference, variants.ToArray());

        var reads = new List<Sequence>();
        await foreach (var r in SimulateReads(mutated, readDepth, readLength))
        {
            reads.Add(r);
        }

        return (reference, variants, reads);
    }
}
