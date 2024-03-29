﻿namespace OpenMedStack.BioSharp.Io.Sam
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Globalization;
    using System.Linq;

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
            var parts = line.Split('\t', StringSplitOptions.TrimEntries);
            return new AlignmentSection(
                parts[0],
                (AlignmentFlag)int.Parse(parts[1], NumberStyles.Integer),
                parts[2],
                int.Parse(parts[3], NumberStyles.Integer),
                byte.Parse(parts[4], NumberStyles.Integer),
                GetOpCodes(parts[5]),
                int.TryParse(parts[6], out var rnext) ? rnext : 0,
                int.Parse(parts[7], NumberStyles.Integer),
                int.Parse(parts[8], NumberStyles.Integer),
                parts[9],
                parts[10],
                parts.Skip(11).Select(AlignmentTag.Parse));
        }

        private static (uint, CigarOp)[] GetOpCodes(string ops)
        {
            return ops.Select(c => (byte)c).Select(b => ((uint)b).Decode()).ToArray();
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
}
