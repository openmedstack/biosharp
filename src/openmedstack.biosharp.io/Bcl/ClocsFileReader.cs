namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using Model.Bcl;

    /**
 * The clocs file format is one of 3 Illumina formats(pos, locs, and clocs) that stores position data exclusively.
 * clocs files store position data for successive clusters, compressed in bins as follows:
 *     Byte 0   : unused
 *     Byte 1-4 : unsigned int numBins
 *     The rest of the file consists of bins/blocks, where a bin consists of an integer
 *     indicating number of blocks, followed by that number of blocks and a block consists
 *     of an x-y coordinate pair.  In otherwords:
 *
 *     for each bin
 *         byte 1: Unsigned int numBlocks
 *         for each block:
 *             byte 1 : byte xRelativeCoordinate
 *             byte 2 : byte yRelativeCoordinate
 *
 *     Actual x and y values are computed using the following algorithm
 *
 *     xOffset = yOffset = 0
 *     imageWidth = 2048
 *     blockSize = 25
 *     maxXbins:Int = Math.Ceiling((double)ImageWidth/(double)blockSize)
 *     for each bin:
 *         for each location:
 *             x = convert.ToSingle(xRelativeCoordinate/10f + xoffset)
 *             y = convert.toSingle(yRelativeCoordinate/10f + yoffset)
 *         if (binIndex > 0 && ((binIndex + 1) % maxXbins == 0)) {
 *            xOffset = 0; yOffset += blockSize
 *         } else xOffset += blockSize
 */
    public class ClocsFileReader : IAsyncEnumerable<IPositionalData>
    {
        private readonly FileInfo _clocsFile; // extends AbstractIlluminaPositionFileReader {

        private readonly int _lane;

        private readonly int _tile;

        //private static int HEADER_SIZE = 5;

        private static readonly int IMAGE_WIDTH = 2048;
        private static readonly int BLOCK_SIZE = 25;
        private static readonly int NUM_BINS_IN_ROW = (int)Math.Ceiling(IMAGE_WIDTH / (double)BLOCK_SIZE);

        /** Total number of bins */
        private readonly long numBins;

        /** An iterator through clocsFile's bytes */
        private readonly BinaryReader byteIterator;

        //mutable vars
        private float xOffset;
        private float yOffset;
        private long currentBin;
        private int numClustersInBin;   //MAX 255
        private long currentClusterInBin;

        public ClocsFileReader(FileInfo clocsFile, int lane, int tile)
        {
            _clocsFile = clocsFile;
            _lane = lane;
            _tile = tile;
            //super(clocsFile);

            byteIterator = new BinaryReader(File.OpenRead(clocsFile.FullName));// MMapBackedIteratorFactory.getByteIterator(HEADER_SIZE, clocsFile);
            _ = byteIterator.ReadByte();

            numBins = byteIterator.ReadUInt32(); //UnsignedTypeUtil.uIntToLong(hbs.getInt());

            xOffset = 0;
            yOffset = 0;
            currentBin = 0;
            startBlock();

            checkAndAdvanceBin();
        }

        /**
         * Grab the next set of offset values, decompress them.
         * @return the position information of the next offset values
         */
        //@Override
        public async IAsyncEnumerator<IPositionalData> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            byte[] buffer = new byte[2];
            while (hasNext())
            {
                var read = await byteIterator.BaseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read != buffer.Length)
                {
                    throw new Exception("Missing position data");
                }
                var xByte = buffer[0];
                var yByte = buffer[1];

                var xPos = /*UnsignedTypeUtil.uByteToInt(xByte)*/ xByte / 10f + xOffset;
                var yPos = /*UnsignedTypeUtil.uByteToInt(yByte)*/ yByte / 10f + yOffset;
                ++currentClusterInBin;
                checkAndAdvanceBin();

                yield return new PositionalData(_tile, _lane, (int)xPos, (int)yPos);
            }
        }

        /** Compute offset for next bin and then increment the bin number and reset block information*/
        private void checkAndAdvanceBin()
        {
            while (currentClusterInBin >= numClustersInBin && currentBin < numBins)
            { //While rather than if statement to skip empty blocks
                if ((currentBin + 1) % NUM_BINS_IN_ROW == 0)
                {
                    xOffset = 0;
                    yOffset += BLOCK_SIZE;
                }
                else
                {
                    xOffset += BLOCK_SIZE;
                }

                currentBin += 1;
                if (currentBin < numBins)
                {
                    startBlock();
                }
            }
        }

        /** Start the next block by reading it's numBlocks byte and setting the currentBlock index to 0 */
        private void startBlock()
        {
            numClustersInBin = byteIterator.ReadByte();// UnsignedTypeUtil.uByteToInt(byteIterator.next());
            currentClusterInBin = 0;
        }

        //@Override
        public bool hasNext()
        {
            var valuesRemain = currentClusterInBin < numClustersInBin || currentBin < (numBins - 1);
            if (!valuesRemain && byteIterator.PeekChar() > -1)
            {
                throw new Exception(
                    $"Read the number of expected bins( {numBins}) but still had more elements in file( {_clocsFile.FullName}) ");
            }
            return valuesRemain;
        }
    }

}