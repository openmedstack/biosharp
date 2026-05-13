using System.IO;
using System.Text;

namespace OpenMedStack.BioSharp.Io.Cram;

/// <summary>
/// Represents the compression header block payload (data series encodings).
/// For this implementation we use EXTERNAL encoding for all data series.
/// </summary>
internal sealed class CramCompressionHeader
{
    // External IDs for each data series
    public const int ExtIdBf = 1;   // BAM flags
    public const int ExtIdCf = 2;   // CRAM flags
    public const int ExtIdRi = 3;   // reference ID
    public const int ExtIdRl = 4;   // read length
    public const int ExtIdAp = 5;   // alignment position delta
    public const int ExtIdRg = 6;   // read group
    public const int ExtIdRn = 7;   // read name
    public const int ExtIdMf = 8;   // mate flags
    public const int ExtIdNs = 9;   // next fragment ref id
    public const int ExtIdNp = 10;  // next fragment position
    public const int ExtIdTs = 11;  // template size
    public const int ExtIdNf = 12;  // number of read features
    public const int ExtIdFn = 13;  // read feature codes
    public const int ExtIdFc = 14;  // feature codes
    public const int ExtIdFp = 15;  // feature position
    public const int ExtIdDl = 16;  // deletion length
    public const int ExtIdBa = 17;  // base (for substitutions/insertions)
    public const int ExtIdQs = 18;  // quality scores
    public const int ExtIdBs = 19;  // base substitution code
    public const int ExtIdIn = 20;  // inserted bases
    public const int ExtIdSc = 21;  // soft clip bases
    public const int ExtIdHc = 22;  // hard clip
    public const int ExtIdPd = 23;  // padding
    public const int ExtIdRs = 24;  // reference skip
    public const int ExtIdMq = 25;  // mapping quality
    public const int ExtIdTn = 26;  // tag names

    /// <summary>
    /// Builds the compression header block for a slice that uses EXTERNAL encoding
    /// for all data series.
    /// </summary>
    public static CramBlock Build()
    {
        using var ms = new MemoryStream();

        // Preservation map (empty - no special flags)
        WriteMap(ms, new System.Collections.Generic.Dictionary<string, byte[]>());

        // Data series encoding map
        var encodings = new System.Collections.Generic.Dictionary<string, byte[]>();
        foreach (var extId in new[]
            {
                ExtIdBf, ExtIdCf, ExtIdRi, ExtIdRl, ExtIdAp, ExtIdRg, ExtIdRn,
                ExtIdMf, ExtIdNs, ExtIdNp, ExtIdTs, ExtIdNf, ExtIdFc, ExtIdFp,
                ExtIdDl, ExtIdBa, ExtIdQs, ExtIdBs, ExtIdIn, ExtIdSc, ExtIdHc,
                ExtIdPd, ExtIdRs, ExtIdMq, ExtIdTn
            })
        {
            // EXTERNAL encoding: codec ID = 1, 4 bytes for external content ID
            var key = DataSeriesKey(extId);
            using var enc = new MemoryStream();
            // Encoding: [codec_id=1(EXTERNAL) as ITF8, param_len as ITF8, extId as ITF8]
            CramEncoding.WriteItf8(enc, 1);             // EXTERNAL codec
            CramEncoding.WriteItf8(enc, 4);             // parameter length (4 bytes for ITF8)
            CramEncoding.WriteItf8(enc, extId);         // external block content ID
            encodings[key] = enc.ToArray();
        }

        WriteMap(ms, encodings);

        // Tag encoding map (empty)
        WriteMap(ms, new System.Collections.Generic.Dictionary<string, byte[]>());

        return CramBlock.CreateRaw(CramBlock.TypeCompressionHeader, 1, ms.ToArray());
    }

    private static string DataSeriesKey(int extId)
        => extId.ToString("D2"); // 2-character key

    private static void WriteMap(Stream s, System.Collections.Generic.Dictionary<string, byte[]> map)
    {
        using var mapMs = new MemoryStream();
        CramEncoding.WriteItf8(mapMs, map.Count);
        foreach (var (key, value) in map)
        {
            var keyBytes = Encoding.ASCII.GetBytes(key.Length >= 2 ? key[..2] : key.PadRight(2));
            mapMs.Write(keyBytes, 0, 2);
            CramEncoding.WriteItf8(mapMs, value.Length);
            mapMs.Write(value, 0, value.Length);
        }

        var mapBytes = mapMs.ToArray();
        CramEncoding.WriteItf8(s, mapBytes.Length);
        s.Write(mapBytes, 0, mapBytes.Length);
    }
}