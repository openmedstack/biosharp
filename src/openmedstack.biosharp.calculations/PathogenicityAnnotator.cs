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
/// Annotates missense variants with pre-computed SIFT and PolyPhen-2 HDIV pathogenicity
/// scores by scanning a dbNSFP v4-format tab-separated flat file loaded into memory.
///
/// Only variants whose <see cref="VariantAnnotation.Consequence"/> is
/// <see cref="VariantConsequence.Missense"/> are annotated; all others return null.
///
/// Missing scores (represented as "." in the file) are stored as null and reported
/// as "." in the <see cref="PathogenicityAnnotation"/> prediction strings.
/// </summary>
public sealed class PathogenicityAnnotator
{
    private readonly record struct VariantKey(string Chrom, int Pos, string Ref, string Alt);

    private readonly Dictionary<VariantKey, PathogenicityAnnotation> _db = new();

    // Column indices in the dbNSFP header; set during LoadAsync
    private int _chrCol = 0;
    private int _posCol = 1;
    private int _refCol = 2;
    private int _altCol = 3;
    private int _siftScoreCol = 4;
    private int _siftPredCol = 5;
    private int _pp2HdivScoreCol = 6;
    private int _pp2HdivPredCol = 7;

    /// <summary>
    /// Loads the dbNSFP flat file from a stream.
    /// The first non-empty line must be a tab-separated header row; its column names are
    /// used to locate the required fields.
    /// Can be called multiple times; each call replaces the previous database.
    /// </summary>
    public async Task Load(Stream stream, CancellationToken cancellationToken = default)
    {
        _db.Clear();

        using var reader = new StreamReader(stream, leaveOpen: true);
        var headerParsed = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            var span = line.AsSpan().Trim();
            if (span.IsEmpty)
            {
                continue;
            }

            if (!headerParsed)
            {
                ParseHeader(span);
                headerParsed = true;
                continue;
            }

            ParseDataLine(span);
        }
    }

    /// <summary>
    /// Looks up a variant in the loaded database.
    /// Returns null unless the variant consequence is Missense AND the variant is found.
    /// </summary>
    public PathogenicityAnnotation? Annotate(VcfVariant variant, VariantAnnotation annotation)
    {
        if (annotation.Consequence != VariantConsequence.Missense)
        {
            return null;
        }

        var key = MakeKey(variant.Chromosome, variant.Position, variant.Reference, variant.Alternate);
        return _db.GetValueOrDefault(key);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void ParseHeader(ReadOnlySpan<char> header)
    {
        // Header columns are tab-separated.
        // Standard dbNSFP v4 column names (case-insensitive matching):
        //   #chr, pos(1-based), ref, alt,
        //   SIFT_score, SIFT_pred, Polyphen2_HDIV_score, Polyphen2_HDIV_pred
        var idx = 0;
        foreach (var range in header.Split('\t'))
        {
            var col = header[range].TrimStart('#');
            if (col.Equals("chr", StringComparison.OrdinalIgnoreCase))
            {
                _chrCol = idx;
            }
            else if (col.StartsWith("pos", StringComparison.OrdinalIgnoreCase))
            {
                _posCol = idx;
            }
            else if (col.Equals("ref", StringComparison.OrdinalIgnoreCase))
            {
                _refCol = idx;
            }
            else if (col.Equals("alt", StringComparison.OrdinalIgnoreCase))
            {
                _altCol = idx;
            }
            else if (col.Equals("SIFT_score", StringComparison.OrdinalIgnoreCase))
            {
                _siftScoreCol = idx;
            }
            else if (col.Equals("SIFT_pred", StringComparison.OrdinalIgnoreCase))
            {
                _siftPredCol = idx;
            }
            else if (col.Equals("Polyphen2_HDIV_score", StringComparison.OrdinalIgnoreCase))
            {
                _pp2HdivScoreCol = idx;
            }
            else if (col.Equals("Polyphen2_HDIV_pred", StringComparison.OrdinalIgnoreCase))
            {
                _pp2HdivPredCol = idx;
            }

            idx++;
        }
    }

    private void ParseDataLine(ReadOnlySpan<char> line)
    {
        // Split by tab into a fixed upper bound; resize if needed
        var maxCols = Math.Max(16, Math.Max(_pp2HdivPredCol, _pp2HdivScoreCol) + 1);
        var ranges = maxCols <= 64
            ? stackalloc Range[64]
            : new Range[maxCols];

        var count = line.Split(ranges, '\t');
        if (count <= Math.Max(_chrCol, Math.Max(_posCol, Math.Max(_refCol, _altCol))))
        {
            return;
        }

        var chrom    = new string(line[ranges[_chrCol]]);
        if (!int.TryParse(line[ranges[_posCol]], out var pos))
        {
            return;
        }

        var refAllele = new string(line[ranges[_refCol]]);
        var alt       = new string(line[ranges[_altCol]]);

        double? siftScore = null;
        var siftPred = ".";
        double? pp2Score = null;
        var pp2Pred = ".";

        if (_siftScoreCol < count)
        {
            var sv = line[ranges[_siftScoreCol]];
            if (!sv.Equals(".", StringComparison.Ordinal) && !sv.IsEmpty)
            {
                if (double.TryParse(sv, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    siftScore = d;
                }
            }
        }

        if (_siftPredCol < count)
        {
            var pv = line[ranges[_siftPredCol]];
            if (!pv.IsEmpty)
            {
                siftPred = new string(pv);
            }
        }

        if (_pp2HdivScoreCol < count)
        {
            var sv = line[ranges[_pp2HdivScoreCol]];
            if (!sv.Equals(".", StringComparison.Ordinal) && !sv.IsEmpty)
            {
                if (double.TryParse(sv, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    pp2Score = d;
                }
            }
        }

        if (_pp2HdivPredCol < count)
        {
            var pv = line[ranges[_pp2HdivPredCol]];
            if (!pv.IsEmpty)
            {
                pp2Pred = new string(pv);
            }
        }

        var key = MakeKey(chrom, pos, refAllele, alt);
        _db.TryAdd(key, new PathogenicityAnnotation(siftScore, siftPred, pp2Score, pp2Pred));
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
