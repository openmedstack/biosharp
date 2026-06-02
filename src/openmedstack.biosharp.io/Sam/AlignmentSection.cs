namespace OpenMedStack.BioSharp.Io.Sam;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

/// <summary>
/// <para>
/// Col Field Type Regexp/Range Brief description
/// 1 QNAME String [!-?A-~]{1,254} Query template NAME
/// 2 FLAG Int [0, 2^16 − 1] bitwise FLAG
/// 3 RNAME String \*|[:rname:∧*=][:rname:]* Reference sequence NAME11
/// 4 POS Int [0, 2^31 − 1] 1-based leftmost mapping POSition
/// 5 MAPQ Int [0, 2^8 − 1] MAPping Quality
/// 6 CIGAR String \*|([0-9]+[MIDNSHPX=])+ CIGAR string
/// 7 RNEXT String \*|=|[:rname:∧*=][:rname:]* Reference name of the mate/next read
/// 8 PNEXT Int [0, 2^31 − 1] Position of the mate/next read
/// 9 TLEN Int [−2^31 + 1, 2^31 − 1] observed Template LENgth
/// 10 SEQ String \*|[A-Za-z=.]+ segment SEQuence
/// 11 QUAL String [!-~]+ ASCII of Phred-scaled base QUALity+33
/// </para>
/// </summary>
public record AlignmentSection
{
    internal AlignmentSection(
        string qname,
        AlignmentFlag flag,
        string rname,
        int position,
        byte mapq,
        (uint, CigarOp)[] cigar,
        int rnext,
        int pnext,
        int templatelength,
        string sequence,
        string quality,
        IEnumerable<AlignmentTag> tags,
        int index = -1)
    {
        QName = qname;
        Flag = flag;
        ReadName = rname;
        Position = position;
        MappingQuality = mapq;
        Cigar = cigar;
        ReferenceIdOfNextSegment = rnext;
        NextPosition = pnext;
        TemplateLength = templatelength;
        Sequence = sequence;
        Quality = quality;
        Tags = ImmutableArray<AlignmentTag>.Empty.AddRange(tags);
        Index = index;
    }

    public string QName { get; }

    public AlignmentFlag Flag { get; }

    public string ReadName { get; }

    public int Position { get; }

    public byte MappingQuality { get; }

    public (uint count, CigarOp op)[] Cigar { get; }

    public int ReferenceIdOfNextSegment { get; }

    public int NextPosition { get; }

    public int TemplateLength { get; }

    public string Sequence { get; }

    public string Quality { get; }

    public ImmutableArray<AlignmentTag> Tags { get; }

    public int Index { get; }

    public static AlignmentSection Parse(string line)
    {
        var span = line.AsSpan();
        Span<Range> ranges = stackalloc Range[64];
        var count = span.Split(ranges, '\t', StringSplitOptions.TrimEntries);

        var tagCount = Math.Max(0, count - 11);
        var tags = new AlignmentTag[tagCount];
        for (var i = 0; i < tagCount; i++)
        {
            tags[i] = AlignmentTag.Parse(span[ranges[11 + i]]);
        }

        return new AlignmentSection(
            new string(span[ranges[0]]),
            (AlignmentFlag)int.Parse(span[ranges[1]]),
            new string(span[ranges[2]]),
            int.Parse(span[ranges[3]]),
            byte.Parse(span[ranges[4]]),
            GetOpCodes(span[ranges[5]]),
            int.TryParse(span[ranges[6]], out var rnext) ? rnext : 0,
            int.Parse(span[ranges[7]]),
            int.Parse(span[ranges[8]]),
            new string(span[ranges[9]]),
            new string(span[ranges[10]]),
            tags);
    }

    private static (uint, CigarOp)[] GetOpCodes(ReadOnlySpan<char> ops)
    {
        if (ops.IsEmpty || (ops.Length == 1 && ops[0] == '*'))
        {
            return [];
        }

        var result = new List<(uint, CigarOp)>(8);
        var i = 0;
        while (i < ops.Length)
        {
            var count = 0u;
            while (i < ops.Length && char.IsAsciiDigit(ops[i]))
            {
                count = count * 10 + (uint)(ops[i++] - '0');
            }

            if (i < ops.Length)
            {
                var cigarOp = ops[i++] switch
                {
                    'M' => CigarOp.Match,
                    'I' => CigarOp.Insertion,
                    'D' => CigarOp.Deletion,
                    'N' => CigarOp.Skip,
                    'S' => CigarOp.SoftClip,
                    'H' => CigarOp.HardClip,
                    'P' => CigarOp.Padding,
                    '=' => CigarOp.Equal,
                    'X' => CigarOp.Difference,
                    _ => CigarOp.Match
                };
                result.Add((count, cigarOp));
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// <para>
    /// Bit Description
    /// 1 0x1 template having multiple segments in sequencing
    /// 2 0x2 each segment properly aligned according to the aligner
    /// 4 0x4 segment unmapped
    /// 8 0x8 next segment in the template unmapped
    /// 16 0x10 SEQ being reverse complemented
    /// 32 0x20 SEQ of the next segment in the template being reverse complemented
    /// 64 0x40 the first segment in the template
    /// 128 0x80 the last segment in the template
    /// 256 0x100 secondary alignment
    /// 512 0x200 not passing filters, such as platform/vendor quality controls
    /// 1024 0x400 PCR or optical duplicate
    /// 2048 0x800 supplementary alignment
    /// /</para>
    /// </summary>
    [Flags]
    public enum AlignmentFlag : ushort
    {
        None = 0,
        MultipleSegments = 1,
        EachSegmentProperlyAligned = 2,
        SegmentUnmapped = 4,
        NextSegmentInTemplateUnmapped = 8,
        SeqBeingReverseComplemented = 16,
        SeqOfNextSegmentInTemplateBeingReverseComplemented = 32,
        FirstSegmentInTemplate = 64,
        LastSegmentInTemplate = 128,
        SecondaryAlignment = 256,
        PcrOrOpticalDuplicate = 1024,
        SupplementaryAlignment = 2048
    }
}
