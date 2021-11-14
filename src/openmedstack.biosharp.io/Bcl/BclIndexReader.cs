/* The code in this folder is migrated from the Java code in the Picard project (https://github.com/broadinstitute/picard).

The code is released under MIT license.*/

namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.IO;
    using System.Threading.Tasks;

    /**
     * Annoyingly, there are two different files with extension .bci in NextSeq output.  This reader handles
     * the file that contains virtual file pointers into a .bcl.bgzf file.  After the header, there is a 64-bit record
     * per tile.
     */
    public class BclIndexReader : IBclIndexReader
    {
        private static readonly int BciHeaderSize = 8;

        private readonly Stream _fileStream;
        private int _nextRecordNumber;

        public BclIndexReader(FileInfo bclFile)
        {
            var path = bclFile.FullName;
            if (!Path.GetExtension(bclFile.Name).Equals(".bci", StringComparison.OrdinalIgnoreCase))
            {
                path += ".bci";
            }
            BciFile = new FileInfo(path);
            _fileStream = new FileStream(
                BciFile.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var headerBytes = new byte[BciHeaderSize];
            _fileStream.Read(headerBytes, 0, BciHeaderSize);

            NumTiles = BitConverter.ToInt32(headerBytes.AsSpan(4, 4));
        }

        public int NumTiles { get; }

        public async Task<BlockOffsetRecord> Get(int recordNumber)
        {
            if (recordNumber < _nextRecordNumber)
            {
                throw new ArgumentException("Can only read forward");
            }

            if (recordNumber > _nextRecordNumber)
            {
                _fileStream.Seek((recordNumber - _nextRecordNumber) * 8, SeekOrigin.Current);
                _nextRecordNumber = recordNumber;
            }

            ++_nextRecordNumber;

            var element = new byte[8];
            _ = await _fileStream.ReadAsync(element).ConfigureAwait(false);
            var virtualFilePointer = BitConverter.ToInt64(element);
            var address = (virtualFilePointer >> ShiftAmount) & AddressMask;
            var offset = (int)(virtualFilePointer & OffsetMask);
            return new(address, offset);
        }

        private const int ShiftAmount = 16;
        private const int OffsetMask = 0xffff;
        private const long AddressMask = 0xFFFFFFFFFFFFL;

        public FileInfo BciFile { get; }
    }
}
