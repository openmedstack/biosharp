using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
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
        int minScore = 0,
        int maxCellCount = 0,
        int bandWidth = -1,
        int xDrop = 0,
        int expectedReferenceStart = -1)
    {
        return Align(
            reference.GetData().Span,
            read.GetData().Span,
            matchScore,
            mismatchPenalty,
            gapOpenPenalty,
            gapExtendPenalty,
            minScore,
            maxCellCount,
            bandWidth,
            xDrop,
            expectedReferenceStart);
    }

    public static AlignmentResult? Align(
        ReadOnlySpan<char> refSeq,
        ReadOnlySpan<char> readSeq,
        int matchScore = 2,
        int mismatchPenalty = -3,
        int gapOpenPenalty = -5,
        int gapExtendPenalty = -2,
        int minScore = 0,
        int maxCellCount = 0,
        int bandWidth = -1,
        int xDrop = 0,
        int expectedReferenceStart = -1)
    {
        var refLen = refSeq.Length;
        var readLen = readSeq.Length;

        if (refLen == 0 || readLen == 0)
        {
            return null;
        }

        if ((long)readLen * matchScore < minScore)
        {
            return null;
        }

        if (TryVectorizedUngappedAlign(
                refSeq,
                readSeq,
                matchScore,
                mismatchPenalty,
                gapOpenPenalty,
                gapExtendPenalty,
                minScore,
                bandWidth,
                xDrop,
                maxCellCount,
                out var ungappedAlignment))
        {
            return ungappedAlignment;
        }

        // In banded mode the effective cell count is proportional to read length × band width,
        // not read length × window length, so use the tighter bound for the budget check.
        var totalCells = bandWidth >= 0
            ? (long)(readLen + 1) * Math.Min(2 * bandWidth + 1, refLen)
            : (long)(readLen + 1) * (refLen + 1);
        if (maxCellCount > 0 && totalCells > maxCellCount)
        {
            throw new InvalidOperationException(
                $"Alignment requires {totalCells} cells which exceeds the configured budget of {maxCellCount}.");
        }

        // Sentinel for "negative infinity" — large negative value
        var negInf = gapOpenPenalty * (readLen + refLen);

        var pool = ArrayPool<int>.Shared;
        var columns = refLen + 1;
        var prevH = pool.Rent(columns);
        var currH = pool.Rent(columns);
        var prevY = pool.Rent(columns);
        var currY = pool.Rent(columns);
        PackedDirectionMatrix? direction = null;
        var wasPruned = false;
        var bestScoreSeen = negInf;

        try
        {
            Array.Clear(prevH, 0, columns);
            Array.Fill(prevY, negInf, 0, columns);
            // Banded mode stores only 2·bandWidth+3 cells per row — O(readLen×bandWidth) total.
            // Full mode stores all (readLen+1)×(refLen+1) cells and is used only when unbanded.
            direction = bandWidth >= 0
                ? PackedDirectionMatrix.CreateBanded(readLen + 1, bandWidth, expectedReferenceStart)
                : PackedDirectionMatrix.CreateFull(readLen + 1, refLen + 1);
            var previousBandStart = 1;
            var previousBandEnd = refLen;

            // Fill DP using rolling rows for H and Y, and a scalar for X.
            for (var row = 1; row <= readLen; row++)
            {
                currH[0] = gapOpenPenalty + (row - 1) * gapExtendPenalty;
                currY[0] = negInf;
                var bandStart = 1;
                var bandEnd = refLen;

                if (bandWidth >= 0)
                {
                    var center = expectedReferenceStart >= 0
                        ? expectedReferenceStart + row
                        : row;
                    bandStart = Math.Max(1, center - bandWidth);
                    bandEnd = Math.Min(refLen, center + bandWidth);
                    wasPruned = wasPruned || bandStart > 1 || bandEnd < refLen;

                    if (bandStart > 1)
                    {
                        currH[bandStart - 1] = negInf;
                        currY[bandStart - 1] = negInf;
                    }

                    if (previousBandStart > bandStart)
                    {
                        prevH[bandStart] = negInf;
                        prevY[bandStart] = negInf;
                    }

                    if (previousBandEnd < bandEnd)
                    {
                        prevH[bandEnd] = negInf;
                        prevY[bandEnd] = negInf;
                    }
                }

                var currentX = bandStart == 1 ? currH[0] : negInf;
                var rowBest = negInf;
                var activeCells = 0;

                for (var col = bandStart; col <= bandEnd; col++)
                {
                    var refChar = refSeq[col - 1];
                    var readChar = readSeq[row - 1];
                    var isMatch = DnaEncoding.AreEqual(refChar, readChar);
                    var diagScore = isMatch ? matchScore : mismatchPenalty;

                    var xFromDiag = currH[col - 1] + gapOpenPenalty;
                    var xFromLeft = currentX + gapExtendPenalty;
                    currentX = Math.Max(xFromDiag, xFromLeft);

                    var yFromDiag = prevH[col] + gapOpenPenalty;
                    var yFromUp = prevY[col] + gapExtendPenalty;
                    var currentY = Math.Max(yFromDiag, yFromUp);
                    currY[col] = currentY;

                    var diagonal = prevH[col - 1] + diagScore;
                    var bestVal = diagonal;
                    if (currentX > bestVal)
                    {
                        bestVal = currentX;
                    }

                    if (currentY > bestVal)
                    {
                        bestVal = currentY;
                    }

                    if (xDrop > 0 && bestScoreSeen > negInf && bestVal < bestScoreSeen - xDrop)
                    {
                        currH[col] = negInf;
                        currY[col] = negInf;
                        direction.Set(row, col, 0);
                        currentX = negInf;
                        wasPruned = true;
                        continue;
                    }

                    currH[col] = bestVal;
                    activeCells++;
                    if (bestVal > rowBest)
                    {
                        rowBest = bestVal;
                    }

                    if (bestVal > bestScoreSeen)
                    {
                        bestScoreSeen = bestVal;
                    }

                    byte dir;
                    if (bestVal == diagonal && bestVal >= currentY && bestVal >= currentX)
                    {
                        dir = 1;
                    }
                    else if (bestVal == currentY && currentY >= currentX)
                    {
                        dir = 2;
                    }
                    else
                    {
                        dir = 3;
                    }

                    direction.Set(row, col, dir);
                }

                if (bandEnd < refLen)
                {
                    currH[bandEnd + 1] = negInf;
                    currY[bandEnd + 1] = negInf;
                }

                previousBandStart = bandStart;
                previousBandEnd = bandEnd;

                if (activeCells == 0)
                {
                    return null;
                }

                if (xDrop > 0 && bestScoreSeen > negInf && rowBest < bestScoreSeen - xDrop)
                {
                    return null;
                }

                (prevH, currH) = (currH, prevH);
                (prevY, currY) = (currY, prevY);
            }

            // Find the best score in the last row (read fully consumed, ref can end anywhere).
            // In banded mode only scan the last row's band — other columns have stale values
            // from earlier rows due to the double-buffer swap.
            var bestInLastCol = negInf;
            var bestCol = 0;
            var scanStart = bandWidth >= 0 ? previousBandStart : 0;
            var scanEnd   = bandWidth >= 0 ? previousBandEnd   : refLen;
            // Column 0 is always valid (pure-insertion prefix path)
            if (prevH[0] > bestInLastCol) { bestInLastCol = prevH[0]; bestCol = 0; }
            for (var col = scanStart; col <= scanEnd; col++)
            {
                if (prevH[col] > bestInLastCol)
                {
                    bestInLastCol = prevH[col];
                    bestCol = col;
                }
            }

            if (bestInLastCol < minScore)
            {
                return null;
            }

        // Traceback from (readLen, bestCol) all the way to (0, ?)
        var tracePool = ArrayPool<char>.Shared;
        var alnRead = tracePool.Rent(readLen + refLen);
        var alnRef  = tracePool.Rent(readLen + refLen);
        var pos = 0;
            var tbI = readLen;
            var tbJ = bestCol;
            var refGapCount = 0;

            while (tbI > 0)
            {
                var dir = direction.Get(tbI, tbJ);

                if (dir == 0)
                {
                    alnRead[pos] = readSeq[tbI - 1];
                    alnRef[pos] = '-';
                    tbI--;
                    pos++;
                    refGapCount++;
                }
                else if (dir == 1)
                {
                    alnRead[pos] = readSeq[tbI - 1];
                    alnRef[pos] = refSeq[tbJ - 1];
                    tbI--;
                    tbJ--;
                    pos++;
                }
                else if (dir == 2)
                {
                    alnRead[pos] = readSeq[tbI - 1];
                    alnRef[pos] = '-';
                    tbI--;
                    pos++;
                    refGapCount++;
                }
                else
                {
                    alnRead[pos] = '-';
                    alnRef[pos] = refSeq[tbJ - 1];
                    tbJ--;
                    pos++;
                }
            }

            var refStart = tbJ;

            Array.Reverse(alnRead, 0, pos);
            Array.Reverse(alnRef, 0, pos);

            var alignedRef  = new string(alnRef, 0, pos);
            var alignedRead = new string(alnRead, 0, pos);
            var visual      = CreateAlignmentStringInternal(alnRead.AsSpan(0, pos), alnRef.AsSpan(0, pos), pos);
            tracePool.Return(alnRead, clearArray: false);
            tracePool.Return(alnRef, clearArray: false);

            return new AlignmentResult(
                alignedRef,
                alignedRead,
                visual,
                bestInLastCol,
                refStart,
                0,
                refLen - refStart - pos + refGapCount,
                wasPruned);
        }
        finally
        {
            direction?.Dispose();
            pool.Return(prevH, clearArray: false);
            pool.Return(currH, clearArray: false);
            pool.Return(prevY, clearArray: false);
            pool.Return(currY, clearArray: false);
        }
    }

    private static bool TryVectorizedUngappedAlign(
        ReadOnlySpan<char> refSeq,
        ReadOnlySpan<char> readSeq,
        int matchScore,
        int mismatchPenalty,
        int gapOpenPenalty,
        int gapExtendPenalty,
        int minScore,
        int bandWidth,
        int xDrop,
        int maxCellCount,
        out AlignmentResult? alignment)
    {
        alignment = null;
        if (bandWidth >= 0 || xDrop > 0 || maxCellCount > 0 || refSeq.Length < readSeq.Length)
        {
            return false;
        }

        if (gapOpenPenalty > mismatchPenalty || gapExtendPenalty > 0)
        {
            return false;
        }

        var exactOffset = refSeq.IndexOf(readSeq, StringComparison.OrdinalIgnoreCase);
        if (exactOffset >= 0)
        {
            alignment = CreateUngappedAlignment(
                refSeq.Slice(exactOffset, readSeq.Length),
                readSeq,
                exactOffset,
                readSeq.Length * matchScore,
                refSeq.Length - exactOffset - readSeq.Length);
            return alignment.Score >= minScore;
        }

        const int maxFastMismatches = 2;
        var bestOffset = -1;
        var bestMismatches = maxFastMismatches + 1;
        if (TryFindAnchoredNearExactHit(refSeq, readSeq, maxFastMismatches, out bestOffset, out bestMismatches))
        {
            var anchoredScore = (readSeq.Length - bestMismatches) * matchScore + bestMismatches * mismatchPenalty;
            if (anchoredScore >= minScore)
            {
                alignment = CreateUngappedAlignment(
                    refSeq.Slice(bestOffset, readSeq.Length),
                    readSeq,
                    bestOffset,
                    anchoredScore,
                    refSeq.Length - bestOffset - readSeq.Length);
                return true;
            }
        }

        var maxOffset = refSeq.Length - readSeq.Length;
        for (var offset = 0; offset <= maxOffset; offset++)
        {
            var mismatches = CountMismatchesUpTo(refSeq.Slice(offset, readSeq.Length), readSeq, bestMismatches - 1);
            if (mismatches < bestMismatches)
            {
                bestMismatches = mismatches;
                bestOffset = offset;
                if (bestMismatches == 0)
                {
                    break;
                }
            }
        }

        if (bestOffset < 0 || bestMismatches > maxFastMismatches)
        {
            return false;
        }

        var score = (readSeq.Length - bestMismatches) * matchScore + bestMismatches * mismatchPenalty;
        if (score < minScore)
        {
            return false;
        }

        alignment = CreateUngappedAlignment(
            refSeq.Slice(bestOffset, readSeq.Length),
            readSeq,
            bestOffset,
            score,
            refSeq.Length - bestOffset - readSeq.Length);
        return true;
    }

    private static bool TryFindAnchoredNearExactHit(
        ReadOnlySpan<char> refSeq,
        ReadOnlySpan<char> readSeq,
        int maxMismatches,
        out int bestOffset,
        out int bestMismatches)
    {
        const int anchorLength = 16;
        bestOffset = -1;
        bestMismatches = maxMismatches + 1;
        if (readSeq.Length < anchorLength || refSeq.Length < readSeq.Length)
        {
            return false;
        }

        Span<int> anchorPositions = stackalloc int[3]
        {
            0,
            Math.Max(0, (readSeq.Length - anchorLength) / 2),
            readSeq.Length - anchorLength
        };

        for (var anchorIndex = 0; anchorIndex < anchorPositions.Length; anchorIndex++)
        {
            var readAnchorStart = anchorPositions[anchorIndex];
            var anchor = readSeq.Slice(readAnchorStart, anchorLength);
            var searchStart = 0;
            while (searchStart <= refSeq.Length - anchorLength)
            {
                var hit = refSeq.Slice(searchStart).IndexOf(anchor, StringComparison.OrdinalIgnoreCase);
                if (hit < 0)
                {
                    break;
                }

                var candidateOffset = searchStart + hit - readAnchorStart;
                searchStart += hit + 1;
                if (candidateOffset < 0 || candidateOffset > refSeq.Length - readSeq.Length)
                {
                    continue;
                }

                var mismatches = CountMismatchesUpTo(refSeq.Slice(candidateOffset, readSeq.Length), readSeq, bestMismatches - 1);
                if (mismatches < bestMismatches)
                {
                    bestMismatches = mismatches;
                    bestOffset = candidateOffset;
                    if (bestMismatches == 0)
                    {
                        return true;
                    }
                }
            }
        }

        return bestOffset >= 0 && bestMismatches <= maxMismatches;
    }

    private static int CountMismatchesUpTo(ReadOnlySpan<char> reference, ReadOnlySpan<char> read, int maxMismatches)
    {
        var mismatches = 0;
        var index = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var vectorWidth = Vector<ushort>.Count;
            var referenceU16 = MemoryMarshal.Cast<char, ushort>(reference);
            var readU16 = MemoryMarshal.Cast<char, ushort>(read);
            for (; index <= read.Length - vectorWidth; index += vectorWidth)
            {
                var referenceVector = new Vector<ushort>(referenceU16.Slice(index, vectorWidth));
                var readVector = new Vector<ushort>(readU16.Slice(index, vectorWidth));
                if (Vector.EqualsAll(referenceVector, readVector))
                {
                    continue;
                }

                for (var local = 0; local < vectorWidth; local++)
                {
                    if (!DnaEncoding.AreEqual(reference[index + local], read[index + local]) && ++mismatches > maxMismatches)
                    {
                        return mismatches;
                    }
                }
            }
        }

        for (; index < read.Length; index++)
        {
            if (!DnaEncoding.AreEqual(reference[index], read[index]) && ++mismatches > maxMismatches)
            {
                return mismatches;
            }
        }

        return mismatches;
    }

    private static AlignmentResult CreateUngappedAlignment(
        ReadOnlySpan<char> reference,
        ReadOnlySpan<char> read,
        int referenceStart,
        int score,
        int rightSoftClip)
    {
        var alignedReference = new string(reference);
        var alignedRead      = new string(read);
        var visualBuf        = ArrayPool<char>.Shared.Rent(read.Length);
        try
        {
            for (var index = 0; index < read.Length; index++)
            {
                visualBuf[index] = DnaEncoding.AreEqual(reference[index], read[index]) ? '|' : 'X';
            }
            return new AlignmentResult(
                alignedReference,
                alignedRead,
                new string(visualBuf, 0, read.Length),
                score,
                referenceStart,
                0,
                rightSoftClip);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(visualBuf, clearArray: false);
        }
    }

    /// <summary>
    /// Packed 2-bit direction matrix for DP traceback.
    ///
    /// Full mode (<see cref="CreateFull"/>): allocates (rows × columns) cells.
    /// Peak memory O(readLen × windowLen) — use only when banding is disabled.
    ///
    /// Banded mode (<see cref="CreateBanded"/>): allocates only 2·bandWidth+3 cells per row,
    /// anchored at the per-row diagonal centre. Peak memory O(readLen × bandWidth).
    /// This eliminates the dominant direction-matrix allocation for typical short-read alignment.
    ///
    /// Row layout (banded): row r covers absolute columns [_rowBandStart[r], _rowBandStart[r] + _storedCols).
    /// Accesses outside that range are silently dropped on Set and return 0 on Get.
    /// </summary>
    private sealed class PackedDirectionMatrix : IDisposable
    {
        private const int BitsPerCell = 2;
        private const int CellsPerWord = sizeof(uint) * 8 / BitsPerCell; // 16 cells per uint

        private readonly uint[] _buffer;
        private readonly int[] _rowBandStart; // per-row absolute column offset; empty in full mode
        private readonly bool _isBanded;
        private readonly int _fullCols;   // full mode: total columns; banded mode: unused
        private readonly int _rowWords;   // banded mode: uint words allocated per row
        private readonly int _storedCols; // banded mode: cells stored per row
        private readonly bool _ownBandStarts;

        /// <summary>
        /// Allocates the full (rows × columns) matrix. Use when banding is disabled.
        /// </summary>
        public static PackedDirectionMatrix CreateFull(int rows, int columns)
        {
            var cellCount = checked(rows * columns);
            var words = (cellCount + CellsPerWord - 1) / CellsPerWord;
            var buf = ArrayPool<uint>.Shared.Rent(words);
            Array.Clear(buf, 0, words);
            return new PackedDirectionMatrix(buf, columns, [], false, 0, 0);
        }

        /// <summary>
        /// Allocates only 2·bandWidth+3 cells per row, anchored at the diagonal centre for each row.
        /// The +3 provides one column of margin on each side of the ±bandWidth band.
        /// </summary>
        public static PackedDirectionMatrix CreateBanded(int rows, int bandWidth, int expectedReferenceStart)
        {
            var storedCols = 2 * bandWidth + 3;
            var rowWords = (storedCols + CellsPerWord - 1) / CellsPerWord;
            var totalWords = checked(rows * rowWords);
            var buf = ArrayPool<uint>.Shared.Rent(totalWords);
            Array.Clear(buf, 0, totalWords);

            var bandStarts = ArrayPool<int>.Shared.Rent(rows);
            for (var r = 0; r < rows; r++)
            {
                var center = expectedReferenceStart >= 0 ? expectedReferenceStart + r : r;
                bandStarts[r] = Math.Max(0, center - bandWidth);
            }

            return new PackedDirectionMatrix(buf, 0, bandStarts, true, rowWords, storedCols);
        }

        private PackedDirectionMatrix(
            uint[] buffer,
            int fullCols,
            int[] rowBandStart,
            bool ownBandStarts,
            int rowWords,
            int storedCols)
        {
            _buffer = buffer;
            _fullCols = fullCols;
            _rowBandStart = rowBandStart;
            _ownBandStarts = ownBandStarts;
            _isBanded = ownBandStarts;
            _rowWords = rowWords;
            _storedCols = storedCols;
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Set(int row, int column, byte value)
        {
            int wordIndex, shift;
            if (_isBanded)
            {
                var localCol = column - _rowBandStart[row];
                if ((uint)localCol >= (uint)_storedCols)
                {
                    return;
                }

                wordIndex = row * _rowWords + localCol / CellsPerWord;
                shift = localCol % CellsPerWord * BitsPerCell;
            }
            else
            {
                var index = row * _fullCols + column;
                wordIndex = index / CellsPerWord;
                shift = index % CellsPerWord * BitsPerCell;
            }
            var mask = 0b11u << shift;
            _buffer[wordIndex] = (_buffer[wordIndex] & ~mask) | ((uint)value << shift);
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public byte Get(int row, int column)
        {
            int wordIndex, shift;
            if (_isBanded)
            {
                var localCol = column - _rowBandStart[row];
                if ((uint)localCol >= (uint)_storedCols)
                {
                    return 0;
                }

                wordIndex = row * _rowWords + localCol / CellsPerWord;
                shift = localCol % CellsPerWord * BitsPerCell;
            }
            else
            {
                var index = row * _fullCols + column;
                wordIndex = index / CellsPerWord;
                shift = index % CellsPerWord * BitsPerCell;
            }
            return (byte)((_buffer[wordIndex] >> shift) & 0b11u);
        }

        public void Dispose()
        {
            ArrayPool<uint>.Shared.Return(_buffer, clearArray: false);
            if (_ownBandStarts)
            {
                ArrayPool<int>.Shared.Return(_rowBandStart, clearArray: false);
            }
        }
    }

    private static string CreateAlignmentStringInternal(ReadOnlySpan<char> aRead, ReadOnlySpan<char> aRef, int len)
    {
        var buf = ArrayPool<char>.Shared.Rent(len);
        try
        {
            for (var i = 0; i < len; i++)
            {
                var refChar  = aRef[i];
                var readChar = aRead[i];
                buf[i] = (refChar == '-' || readChar == '-') ? ' '
                       : DnaEncoding.AreEqual(refChar, readChar) ? '|' : 'X';
            }
            return new string(buf, 0, len);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buf, clearArray: false);
        }
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
