namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
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
    public class ClocsFileReader : ILocationReader
    {
        private readonly MemoryStream _fileContent = new ();
        private readonly FileInfo _clocsFile; // extends AbstractIlluminaPositionFileReader {
        private static readonly int ImageWidth = 2048;
        private static readonly int BlockSize = 25;
        private static readonly int NumBinsInRow = (int)Math.Ceiling(ImageWidth / (double)BlockSize);

        /** Total number of bins */
        private readonly long _numBins;

        /** An iterator through clocsFile's bytes */
        private readonly BinaryReader _byteIterator;

        //mutable vars
        private float _xOffset;
        private float _yOffset;
        private long _currentBin;
        private int _numClustersInBin;   //MAX 255
        private long _currentClusterInBin;

        public ClocsFileReader(FileInfo clocsFile)
        {
            _clocsFile = clocsFile;
            using var fs = File.OpenRead(clocsFile.FullName);
            fs.CopyTo(_fileContent);
            _fileContent.Position = 0;
            _byteIterator = new BinaryReader(_fileContent);
            _ = _byteIterator.ReadByte();

            _numBins = _byteIterator.ReadUInt32();

            _xOffset = 0;
            _yOffset = 0;
            _currentBin = 0;
            StartBlock();

            CheckAndAdvanceBin();
        }

        /**
         * Grab the next set of offset values, decompress them.
         * @return the position information of the next offset values
         */
        //@Override
        public async IAsyncEnumerator<IPositionalData> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            byte[] buffer = new byte[2];
            while (HasNext())
            {
                var read = await _byteIterator.BaseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read != buffer.Length)
                {
                    throw new Exception("Missing position data");
                }
                var xByte = buffer[0];
                var yByte = buffer[1];

                var xPos = /*UnsignedTypeUtil.uByteToInt(xByte)*/ xByte / 10f + _xOffset;
                var yPos = /*UnsignedTypeUtil.uByteToInt(yByte)*/ yByte / 10f + _yOffset;
                ++_currentClusterInBin;
                CheckAndAdvanceBin();

                yield return new PositionalData((int)xPos, (int)yPos);
            }
        }

        /** Compute offset for next bin and then increment the bin number and reset block information*/
        private void CheckAndAdvanceBin()
        {
            while (_currentClusterInBin >= _numClustersInBin && _currentBin < _numBins)
            { //While rather than if statement to skip empty blocks
                if ((_currentBin + 1) % NumBinsInRow == 0)
                {
                    _xOffset = 0;
                    _yOffset += BlockSize;
                }
                else
                {
                    _xOffset += BlockSize;
                }

                _currentBin += 1;
                if (_currentBin < _numBins)
                {
                    StartBlock();
                }
            }
        }

        /** Start the next block by reading it's numBlocks byte and setting the currentBlock index to 0 */
        private void StartBlock()
        {
            _numClustersInBin = _byteIterator.ReadByte();// UnsignedTypeUtil.uByteToInt(byteIterator.next());
            _currentClusterInBin = 0;
        }

        //@Override
        private bool HasNext()
        {
            var valuesRemain = _currentClusterInBin < _numClustersInBin || _currentBin < _numBins - 1;
            if (!valuesRemain && _byteIterator.PeekChar() > -1)
            {
                throw new Exception(
                    $"Read the number of expected bins( {_numBins}) but still had more elements in file( {_clocsFile.FullName}) ");
            }
            return valuesRemain;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _fileContent.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }

}