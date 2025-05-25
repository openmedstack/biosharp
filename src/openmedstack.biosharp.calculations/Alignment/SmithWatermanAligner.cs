namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Linq;
using Model;

/// <summary>
/// Smith-Waterman-style aligner adapted for variant calling.
/// 
/// Performs semi-global (glocal) alignment: the read is aligned globally
/// (every read base must be placed), while the reference is aligned locally
/// (leading and trailing reference bases may be skipped with no penalty).
/// 
/// Uses affine gap penalties: opening a gap costs gapOpenPenalty,
/// each subsequent gap-base in the same gap costs gapExtendPenalty.
/// </summary>
public static class SmithWatermanAligner
{
    /// <summary>
    /// Align a read sequence against a reference for variant calling.
    /// Returns null if no alignment meets the minimum score threshold.
    /// </summary>
    public static AlignmentResult? Align(
        Sequence reference,
        Sequence read,
        int matchScore = 2,
        int mismatchPenalty = -3,
        int gapOpenPenalty = -5,
        int gapExtendPenalty = -2,
        int minScore = 0)
    {
        var refSeq = reference.GetData()!.Span;
        var readSeq = read.GetData()!.Span;
        var refLen = refSeq.Length;
        var readLen = readSeq.Length;

        if (refLen == 0 || readLen == 0) return null;

        // Glocal alignment: global on read, local on reference.
        // H: match/mismatch scores
        // X: best score ending with gap in ref
        // Y: best score ending with gap in read
        var h = new int[readLen + 1, refLen + 1];
        var x = new int[readLen + 1, refLen + 1];
        var y = new int[readLen + 1, refLen + 1];
        var direction = new byte[readLen + 1, refLen + 1];

        // Sentinel for "negative infinity" — large negative value
        var negInf = gapOpenPenalty * (readLen + refLen);

        // Initialisation — glocal: no penalty for ref start/end, full penalty for read start
        // First row: ref starts for free (local)
        for (var j = 0; j <= refLen; j++)
            h[0, j] = 0; // first row already 0 from CLR

        // First column: read start must align (global), penalise unaligned read bases
        for (var i = 1; i <= readLen; i++)
        {
            h[i, 0] = gapOpenPenalty + (i - 1) * gapExtendPenalty;
            x[i, 0] = gapOpenPenalty + (i - 1) * gapExtendPenalty;
            y[i, 0] = negInf; // impossible to have gap in ref at start (no ref consumed)
        }
        // X[0, j] and Y[0, j] already negInf from CLR... no, CLR initialises to 0.
        // Set X[0, j] and Y[0, j] for all j: these mean "ending with gap in read at row 0"
        // Y[0, j] = gap in ref at row 0 — impossible (read has 0 bases consumed)
        // X[0, j] = gap in ref at row 0 — similarly impossible... wait
        // Actually X[i,j] = "best score ending with gap in read (ref consumed, read didn't)"
        // So X[0,j] = i=0 means 0 read consumed, so we can't have a gap in read → negInf
        // Y[0,j] = gap in ref at 0 read consumed → negInf
        // X[i,0] = gap in read at 0 ref consumed → impossible
        // Y[i,0] = gap in ref at i read consumed → gap in ref → need to have consumed ref... hmm
        // Actually Y[i,0] = gap in ref → ref consumed → need ref consumed > 0 → j must be > 0
        // So Y[i,0] for j=0 → negInf makes sense
        // Similarly X[0,j] for i=0 → X consumes read base... X[i,j] means "last operation consumed ref but not read"
        // So X[0,j] = negInf (no read consumed, can't be in gap)

        // Fill matrices
        for (var row = 1; row <= readLen; row++)
        {
            for (var col = 1; col <= refLen; col++)
            {
                var refChar = refSeq[col - 1];
                var readChar = readSeq[row - 1];
                var isMatch = char.ToUpper(refChar) == char.ToUpper(readChar);
                var diagScore = isMatch ? matchScore : mismatchPenalty;

                // X[row,col]: gap in read (ref base consumed, read base NOT consumed)
                // Can come from: diag (start new gap) OR left (extend existing gap)
                var xFromDiag = h[row, col - 1] + gapOpenPenalty;
                var xFromLeft = x[row, col - 1] + gapExtendPenalty;
                x[row, col] = Math.Max(xFromDiag, xFromLeft);

                // Y[row,col]: gap in ref (read base consumed, ref base NOT consumed)
                // Can come from: diag (start new gap) OR up (extend existing gap)
                var yFromDiag = h[row - 1, col] + gapOpenPenalty;
                var yFromUp = y[row - 1, col] + gapExtendPenalty;
                y[row, col] = Math.Max(yFromDiag, yFromUp);

                // H[row,col]: best score ending at (row, col)
                var bestVal = diagScore + h[row - 1, col - 1];
                if (x[row, col] > bestVal) bestVal = x[row, col];

                if (y[row, col] > bestVal) bestVal = y[row, col];

                h[row, col] = bestVal;

                // Record direction: need to know which path produced bestVal
                byte dir = 0;
                if (bestVal == h[row - 1, col - 1] + diagScore &&
                    bestVal >= y[row, col] && bestVal >= x[row, col])
                    dir = 1; // diagonal: match/mismatch
                else if (bestVal == y[row, col] && y[row, col] >= x[row, col])
                    dir = 2; // up: gap in ref (insertion in read)
                else
                    dir = 3; // left: gap in read (deletion in read)

                // Handle ties — prefer diagonal > up > left (standard convention)
                // Already: if diag ties, it's checked first. If Y ties with diag but also X ties,
                // the first condition catches diag. If Y beats X, dir=2. Otherwise dir=3.

                direction[row, col] = dir;
            }
        }

        // Find the best score in the last column (read fully consumed, ref can end anywhere)
        var bestInLastCol = negInf;
        var bestCol = 0;
        for (var col = 0; col <= refLen; col++)
            if (h[readLen, col] > bestInLastCol)
            {
                bestInLastCol = h[readLen, col];
                bestCol = col;
            }

        if (bestInLastCol < minScore) return null;

        // Traceback from (readLen, bestCol) all the way to (0, ?)
        // All read bases must be consumed → go until row = 0
        var alnRead = new char[readLen + refLen];
        var alnRef = new char[readLen + refLen];
        var pos = 0;
        var tbI = readLen;
        var tbJ = bestCol;

        while (tbI > 0)
        {
            var dir = direction[tbI, tbJ];

            if (dir == 0)
            {
                // No more directions set — this means the path went through
                // a cell where bestVal ≤ negInf or similar. Just consume remaining read.
                alnRead[pos] = readSeq[tbI - 1];
                alnRef[pos] = '-'; // gap in ref (read base has no ref base to align with)
                tbI--;
                pos++;
            }
            else if (dir == 1) // diagonal: match/mismatch
            {
                alnRead[pos] = readSeq[tbI - 1];
                alnRef[pos] = refSeq[tbJ - 1];
                tbI--;
                tbJ--;
                pos++;
            }
            else if (dir == 2) // gap in ref (insertion in read)
            {
                alnRead[pos] = readSeq[tbI - 1];
                alnRef[pos] = '-';
                tbI--;
                pos++;
            }
            else if (dir == 3) // gap in read (deletion in read)
            {
                alnRead[pos] = '-';
                alnRef[pos] = refSeq[tbJ - 1];
                tbJ--;
                pos++;
            }
        }

        // After traceback, if there are still ref bases at the beginning, they're
        // unaligned (but that's fine — we did glocal ref alignment).
        // tbJ should be >= 0; any remaining ref bases before tbJ are "skipped"
        var refStart = tbJ;

        // Reverse to get correct 5'→3' order
        Array.Reverse(alnRead, 0, pos);
        Array.Reverse(alnRef, 0, pos);

        return new AlignmentResult(
            new string(alnRef, 0, pos),
            new string(alnRead, 0, pos),
            CreateAlignmentStringInternal(alnRead, alnRef, pos),
            bestInLastCol,
            refStart,
            0, // leftSoftClip: all read consumed (glocal on read)
            refLen - refStart - pos + alnRef.Count(c => c == '-'));
    }

    private static string CreateAlignmentStringInternal(char[] aRead, char[] aRef, int len)
    {
        var sb = new System.Text.StringBuilder(len);
        for (var i = 0; i < len; i++)
        {
            var refChar = aRef[i];
            var readChar = aRead[i];

            if (refChar == '-' || readChar == '-')
                sb.Append(' ');
            else if (char.ToUpper(refChar) == char.ToUpper(readChar))
                sb.Append('|');
            else
                sb.Append('X');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Creates a visual alignment string from aligned reference and read.
    /// '|' for match, 'X' for mismatch, ' ' for gap in either sequence.
    /// </summary>
    public static string CreateAlignmentString(AlignmentResult result)
    {
        return result.VisualString;
    }
}
