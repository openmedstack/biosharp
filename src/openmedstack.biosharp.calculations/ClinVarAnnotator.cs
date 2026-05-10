namespace OpenMedStack.BioSharp.Calculations;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Model;
using Model.Vcf;

/// <summary>
/// Annotates variants with ClinVar clinical significance information by scanning a
/// ClinVar VCF file (plain text or gzip-compressed) loaded into memory.
///
/// Matching is performed by exact (chrom, pos, ref, alt) comparison after normalising
/// chromosome names (stripping/adding "chr" prefix as needed).
///
/// INFO fields parsed: CLNSIG, CLNDN, CLNREVSTAT.
/// Missing variants return null rather than throwing.
/// </summary>
public sealed class ClinVarAnnotator
{
    private readonly record struct VariantKey(string Chrom, int Pos, string Ref, string Alt);

    private readonly Dictionary<VariantKey, ClinVarAnnotation> _db = new();

    /// <summary>
    /// Loads the ClinVar VCF database from a stream into memory.
    /// Can be called multiple times; each call replaces the previous database.
    /// </summary>
    public async Task LoadAsync(Stream stream, CancellationToken cancellationToken = default)
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
    /// Looks up a variant in the loaded ClinVar database.
    /// Returns null if the variant is not present or the database has not been loaded.
    /// </summary>
    public ClinVarAnnotation? Annotate(VcfVariant variant)
    {
        var key = MakeKey(variant.Chromosome, variant.Position, variant.Reference, variant.Alternate);
        return _db.TryGetValue(key, out var ann) ? ann : null;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void ParseVcfLine(ReadOnlySpan<char> line, out VariantKey key, out ClinVarAnnotation? ann)
    {
        key = default;
        ann = null;

        // CHROM POS ID REF ALT QUAL FILTER INFO
        Span<Range> ranges = stackalloc Range[8];
        var count = line.Split(ranges, '\t');
        if (count < 8)
        {
            return;
        }

        var chrom = new string(line[ranges[0]]);
        if (!int.TryParse(line[ranges[1]], out var pos))
        {
            return;
        }

        var refAllele = new string(line[ranges[3]]);
        var alt = new string(line[ranges[4]]);
        var info = line[ranges[7]];

        key = MakeKey(chrom, pos, refAllele, alt);

        // Parse INFO for CLNSIG, CLNDN, CLNREVSTAT
        ParseInfoFields(info, out var clnSig, out var clnDn, out var clnRevStat);
        if (clnSig is not null)
        {
            ann = new ClinVarAnnotation(clnSig, clnDn ?? "", clnRevStat ?? "");
        }
    }

    private static void ParseInfoFields(
        ReadOnlySpan<char> info,
        out string? clnSig,
        out string? clnDn,
        out string? clnRevStat)
    {
        clnSig = null;
        clnDn = null;
        clnRevStat = null;

        Span<Range> fields = stackalloc Range[64];
        var count = info.Split(fields, ';');

        for (var i = 0; i < count; i++)
        {
            var field = info[fields[i]];
            var eqIdx = field.IndexOf('=');
            if (eqIdx < 0)
            {
                continue;
            }

            var key = field[..eqIdx];
            var value = new string(field[(eqIdx + 1)..]);

            if (key.Equals("CLNSIG", StringComparison.OrdinalIgnoreCase))
            {
                clnSig = value;
            }
            else if (key.Equals("CLNDN", StringComparison.OrdinalIgnoreCase))
            {
                clnDn = value;
            }
            else if (key.Equals("CLNREVSTAT", StringComparison.OrdinalIgnoreCase))
            {
                clnRevStat = value;
            }
        }
    }

    private static VariantKey MakeKey(string chrom, int pos, string @ref, string alt) =>
        new(NormaliseChrom(chrom), pos, @ref.ToUpperInvariant(), alt.ToUpperInvariant());

    private static string NormaliseChrom(string chrom)
    {
        // Accept both "chr1" and "1" styles; normalise to lowercase
        var s = chrom.AsSpan();
        return s.StartsWith("chr", StringComparison.OrdinalIgnoreCase)
            ? new string(s).ToLowerInvariant()
            : ("chr" + chrom).ToLowerInvariant();
    }
}
