namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;

    /**
 * Illumina uses an algorithm described in "Theory of RTA" that determines whether or not a cluster passes filter("PF") or not.
 * These values are written as sequential bytes to Filter Files.  The structure of a filter file is as follows:
 * Bytes 0-3  : 0
 * Bytes 4-7  : unsigned int version
 * Bytes 8-11 : unsigned int numClusters
 */
    public class FilterFileReader : IEnumerable<bool>
    {
        /** Number of bytes in the files header that will be skipped by the iterator*/
        private const int HeaderSize = 12;

        /** Expected Version */
        public const int ExpectedVersion = 3;

        /** Iterator over each cluster in the FilterFile */
        private readonly Stream _bbIterator;

        /** Version number found in the FilterFile, this should equal 3 */
        public int Version;

        /** The number of cluster's pf values stored in this file */
        public long NumClusters { get; }

        /** Byte representing a cluster failing filter(not a PF read), we test this exactly at
     * the moment but technically the standard  may be to check only lowest significant bit */
        private const byte FailedFilter = 0x00;

        /** Byte representing a cluster passing filter(a PF read), we test this exactly at
     * the moment but technically the standard  may be to check only lowest significant bit */
        private const byte PassedFilter = 0x01;

        /** The index of the current cluster within the file*/
        private int _currentCluster;

        public FilterFileReader(FileInfo file)
        {
            _bbIterator = File.Open(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
            // MMapBackedIteratorFactory.getByteIterator(HEADER_SIZE, file);
            byte[] headerBuf = new byte[HeaderSize]; //bbIterator.getHeaderBytes();
            var read = _bbIterator.Read(headerBuf);
            if (read != HeaderSize)
            {
                throw new Exception("Invalid filter file");
            }

            for (int i = 0; i < 4; i++)
            {
                if (headerBuf[i] != 0)
                {
                    throw new Exception(
                        $"The first four bytes of a Filter File should be 0 but byte {i} was {headerBuf[i]} in file {file.FullName}");
                }
            }

            Version = BitConverter.ToInt32(headerBuf.AsSpan(4, 4));
            if (Version != ExpectedVersion)
            {
                throw new Exception(
                    $"Expected version is {ExpectedVersion} but version found was {Version} in file {file.FullName}");
            }

            NumClusters =
                BitConverter.ToUInt32(headerBuf.AsSpan(8, 4)); // UnsignedTypeUtil.uIntToLong(headerBuf.getInt());
            if (_bbIterator.Length != NumClusters + HeaderSize)
            {
                throw new Exception($"Filter file size mismatch in file {file.FullName}");
            }

            _currentCluster = 0;
        }

        /// <inheritdoc />
        public IEnumerator<bool> GetEnumerator()
        {
            while (_currentCluster < NumClusters)
            {
                byte value = (byte)_bbIterator.ReadByte();
                _currentCluster += 1;
                yield return value switch
                {
                    PassedFilter => true,
                    FailedFilter => false,
                    _ => throw new Exception($"Didn't recognized PF Byte (0x{value:X}) for element ({_currentCluster})")
                };
            }
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}