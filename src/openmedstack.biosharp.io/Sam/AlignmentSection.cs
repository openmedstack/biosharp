﻿namespace OpenMedStack.BioSharp.Io.Sam
{
    using System;
    using System.Globalization;

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
        private AlignmentSection() { }

        public string QName { get; init; }

        public AlignmentFlag Flag { get; init; }

        public string RName { get; init; }

        public int Position { get; init; }

        public int MapQ { get; init; }

        public string Cigar { get; init; }

        public string RNext { get; init; }

        public int PNext { get; init; }

        public int TemplateLength { get; init; }

        public string Sequence { get; init; }

        public string Quality { get; init; }

        public static AlignmentSection Parse(string line)
        {
            // r003 0 ref 9 30 5S6M * 0 0 GCCTAAGCTAA * SA:Z:ref,29,-,6H5M,17,0;
            var parts = line.Split('\t', StringSplitOptions.TrimEntries);
            return new AlignmentSection
            {
                QName = parts[0],
                Flag = (AlignmentFlag)int.Parse(parts[1], NumberStyles.Integer),
                RName = parts[2],
                Position = int.Parse(parts[3], NumberStyles.Integer),
                MapQ = int.Parse(parts[4], NumberStyles.Integer),
                Cigar = parts[5],
                RNext = parts[6],
                PNext = int.Parse(parts[7], NumberStyles.Integer),
                TemplateLength = int.Parse(parts[8], NumberStyles.Integer),
                Sequence = parts[9],
                Quality = parts[10]
            };
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
        public enum AlignmentFlag
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