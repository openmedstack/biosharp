namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

/// <summary>
/// Annotates variants with population allele frequency information by scanning a
/// VCF-formatted population frequency database (e.g. gnomAD, ExAC) in memory.
///
/// <para>
/// Matching is performed by exact (chrom, pos, ref, alt) comparison after
/// normalising chromosome names (stripping/adding "chr" prefix as needed).
/// </para>
///
/// <para>
/// The annotator reads the database once into a lookup dictionary, then
/// annotates all variants in a single in-memory pass, avoiding repeated I/O.
/// </para>
///
/// <para>
/// INFO fields recognised: <c>AF</c>, <c>AF_popmax</c>, <c>AN</c>, <c>AC</c>.
/// Missing fields default to zero rather than null.
/// </para>
/// </summary>
public sealed class PopulationFrequencyAnnotator
{
    // Key = "chrom:pos:ref:alt" (lower-case chrom, upper-case alleles)
    private readonly record struct VariantKey(string Chrom, int Pos, string Ref, string Alt);

    private readonly record struct PopEntry(double Af, double AfPopmax, int An, int Ac);

    /// <summary>
    /// Annotates an enumerable of variants against a VCF population database read from
    /// <paramref name="populationVcfStream"/>.
    /// </summary>
    /// <param name="variants">Variants to annotate.</param>
    /// <param name="populationVcfStream">
    /// Readable stream of a plain-text or gzip-compressed VCF file with AF/AN/AC INFO fields.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// One <see cref="PopulationFrequencyAnnotation"/> per input variant, in the same order.
    /// Variants absent from the database receive zero-valued frequency fields.
    /// </returns>
    public static async IAsyncEnumerable<PopulationFrequencyAnnotation> Annotate(
        IEnumerable<LocalVariantResult> variants,
        Stream populationVcfStream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Load the entire population VCF into a dictionary (typically a few hundred MB)
        var db = await LoadDatabase(populationVcfStream, cancellationToken).ConfigureAwait(false);

        foreach (var variant in variants)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = MakeKey(variant.Chromosome, variant.Position, variant.Reference, variant.Alternate);

            if (db.TryGetValue(key, out var entry))
            {
                yield return new PopulationFrequencyAnnotation(
                    variant, entry.Af, entry.AfPopmax, entry.An, entry.Ac);
            }
            else
            {
                yield return new PopulationFrequencyAnnotation(variant, 0.0, 0.0, 0, 0);
            }
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static async System.Threading.Tasks.Task<Dictionary<VariantKey, PopEntry>> LoadDatabase(
        Stream stream,
        CancellationToken ct)
    {
        var db = new Dictionary<VariantKey, PopEntry>();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null)
            {
                break;
            }

            var span = line.AsSpan();
            if (span.IsEmpty || span[0] == '#')
            {
                continue;
            }

            ParseVcfLine(span, out var chrom, out var pos, out var refAllele, out var alt, out var info);
            if (chrom.IsEmpty || refAllele.IsEmpty || alt.IsEmpty)
            {
                continue;
            }

            ParseInfoFields(info, out var af, out var afPopmax, out var an, out var ac);

            var key = MakeKey(new string(chrom), pos, new string(refAllele), new string(alt));
            db.TryAdd(key, new PopEntry(af, afPopmax, an, ac));
        }

        return db;
    }

    private static void ParseVcfLine(
        ReadOnlySpan<char> line,
        out ReadOnlySpan<char> chrom,
        out int pos,
        out ReadOnlySpan<char> refAllele,
        out ReadOnlySpan<char> alt,
        out ReadOnlySpan<char> info)
    {
        chrom = default;
        pos = 0;
        refAllele = default;
        alt = default;
        info = default;

        // CHROM POS ID REF ALT QUAL FILTER INFO ...
        Span<Range> ranges = stackalloc Range[8];
        var count = line.Split(ranges, '\t');
        if (count < 8)
        {
            return;
        }

        chrom = line[ranges[0]];
        if (!int.TryParse(line[ranges[1]], out pos))
        {
            return;
        }

        refAllele = line[ranges[3]];
        alt = line[ranges[4]];
        info = line[ranges[7]];
    }

    private static void ParseInfoFields(
        ReadOnlySpan<char> info,
        out double af,
        out double afPopmax,
        out int an,
        out int ac)
    {
        af = 0;
        afPopmax = 0;
        an = 0;
        ac = 0;

        // INFO is semicolon-separated key=value pairs
        Span<Range> fields = stackalloc Range[64];
        var fieldCount = info.Split(fields, ';');

        for (var i = 0; i < fieldCount; i++)
        {
            var field = info[fields[i]];
            var eqIdx = field.IndexOf('=');
            if (eqIdx < 0)
            {
                continue;
            }

            var key = field[..eqIdx];
            var value = field[(eqIdx + 1)..];

            // Take first value only (for multi-allelic AF=0.01,0.02 take the first)
            var commaIdx = value.IndexOf(',');
            if (commaIdx >= 0)
            {
                value = value[..commaIdx];
            }

            if (key.Equals("AF", StringComparison.OrdinalIgnoreCase))
            {
                double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out af);
            }
            else if (key.Equals("AF_popmax", StringComparison.OrdinalIgnoreCase))
            {
                double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out afPopmax);
            }
            else if (key.Equals("AN", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(value, out an);
            }
            else if (key.Equals("AC", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(value, out ac);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static VariantKey MakeKey(string chrom, int pos, string refAllele, string alt)
    {
        // Normalise chromosome: lowercase; strip leading "chr" for matching
        var normChrom = NormaliseChrom(chrom);
        return new VariantKey(normChrom, pos, refAllele.ToUpperInvariant(), alt.ToUpperInvariant());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string NormaliseChrom(ReadOnlySpan<char> chrom)
    {
        // Strips "chr" prefix and lowercases for normalisation
        if (chrom.StartsWith("chr", StringComparison.OrdinalIgnoreCase))
        {
            chrom = chrom[3..];
        }

        return new string(chrom).ToLowerInvariant();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string NormaliseChrom(string chrom)
    {
        return NormaliseChrom(chrom.AsSpan());
    }
}
