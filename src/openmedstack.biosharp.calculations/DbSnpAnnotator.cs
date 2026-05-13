namespace OpenMedStack.BioSharp.Calculations;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Model;
using Model.Vcf;

/// <summary>
/// Annotates variants with dbSNP rsID information by scanning a dbSNP VCF file
/// (plain text or gzip-compressed) loaded into memory.
///
/// The rsID is extracted from the VCF ID column (e.g. "rs1234567") or from the
/// RS INFO field if the ID column is missing.
/// Matching is performed by exact (chrom, pos, ref, alt) comparison after normalising
/// chromosome names.
/// Missing variants return null.
/// </summary>
public sealed class DbSnpAnnotator
{
    private readonly record struct VariantKey(string Chrom, int Pos, string Ref, string Alt);

    private readonly Dictionary<VariantKey, DbSnpAnnotation> _db = new();

    /// <summary>
    /// Loads the dbSNP VCF database from a stream into memory.
    /// Can be called multiple times; each call replaces the previous database.
    /// </summary>
    public async Task Load(Stream stream, CancellationToken cancellationToken = default)
    {
        _db.Clear();

        using var reader = new StreamReader(stream, leaveOpen: true);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            var span = line.AsSpan();
            if (span.IsEmpty || span[0] == '#')
            {
                continue;
            }

            ParseVcfLine(span, out var key, out var ann);
            if (ann is not null)
            {
                _db.TryAdd(key, ann);
            }
        }
    }

    /// <summary>
    /// Looks up a variant in the loaded dbSNP database.
    /// Returns null if the variant is not present or the database has not been loaded.
    /// </summary>
    public DbSnpAnnotation? Annotate(VcfVariant variant)
    {
        var key = MakeKey(variant.Chromosome, variant.Position, variant.Reference, variant.Alternate);
        return _db.GetValueOrDefault(key);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void ParseVcfLine(ReadOnlySpan<char> line, out VariantKey key, out DbSnpAnnotation? ann)
    {
        key = default;
        ann = null;

        // CHROM POS ID REF ALT QUAL FILTER INFO
        Span<Range> ranges = stackalloc Range[8];
        var count = line.Split(ranges, '\t');
        if (count < 4)
        {
            return;
        }

        var chrom = new string(line[ranges[0]]);
        if (!int.TryParse(line[ranges[1]], out var pos))
        {
            return;
        }

        var id = new string(line[ranges[2]]);
        var refAllele = new string(line[ranges[3]]);
        var alt = count > 4 ? new string(line[ranges[4]]) : ".";

        // Prefer the ID column rsID; fall back to RS INFO field
        var rsId = string.Empty;
        if (id.StartsWith("rs", StringComparison.OrdinalIgnoreCase))
        {
            rsId = id;
        }
        else if (count >= 8)
        {
            // Try to extract RS= from INFO field
            rsId = ExtractRsFromInfo(line[ranges[7]]);
        }

        if (string.IsNullOrEmpty(rsId))
        {
            return;
        }

        if (alt == ".")
        {
            return;
        }

        key = MakeKey(chrom, pos, refAllele, alt);
        ann = new DbSnpAnnotation(rsId);
    }

    private static string ExtractRsFromInfo(ReadOnlySpan<char> info)
    {
        Span<Range> fields = stackalloc Range[64];
        var count = info.Split(fields, ';');
        for (var i = 0; i < count; i++)
        {
            var field = info[fields[i]];
            if (!field.StartsWith("RS=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var val = field[3..];
            // Prepend "rs" if it's a numeric ID
            return long.TryParse(val, out _) ? $"rs{new string(val)}" : new string(val);
        }

        return string.Empty;
    }

    private static VariantKey MakeKey(string chrom, int pos, string @ref, string alt) =>
        new(NormaliseChrom(chrom), pos, @ref.ToUpperInvariant(), alt.ToUpperInvariant());

    private static string NormaliseChrom(string chrom)
    {
        var s = chrom.AsSpan();
        return s.StartsWith("chr", StringComparison.OrdinalIgnoreCase)
            ? new string(s).ToLowerInvariant()
            : ("chr" + chrom).ToLowerInvariant();
    }
}
