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
    public class BclIndexReader
    {
        private static readonly int _bciHeaderSize = 8;
        private static readonly int _bciVersion = 0;

        //private BinaryFileIterator<Long> bciIterator;
        private readonly int _numTiles;
        private readonly FileInfo _bciFile;
        private readonly Stream _fileStream;
        private int _nextRecordNumber = 0;

        public BclIndexReader(FileInfo bclFile)
        {
            _bciFile = new FileInfo(bclFile.FullName + ".bci");
            _fileStream = File.OpenRead(_bciFile.FullName);
            var headerBytes = new byte[_bciHeaderSize];
            _fileStream.Read(headerBytes, 0, _bciHeaderSize);
            var actualVersion = BitConverter.ToInt32(headerBytes.AsSpan(0, 4));
            if (actualVersion != _bciVersion)
            {
                throw new Exception($"Unexpected version number {actualVersion} in {_bciFile.FullName}");
            }

            _numTiles = BitConverter.ToInt32(headerBytes.AsSpan(4, 4));
        }

        public int NumTiles
        {
            get { return _numTiles; }
        }

        public async Task<long> Get(int recordNumber)
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

            byte[] element = new byte[8];
            _ = await _fileStream.ReadAsync(element).ConfigureAwait(false);
            return BitConverter.ToInt64(element);
        }

        public FileInfo BciFile
        {
            get { return _bciFile; }
        }
    }
}
