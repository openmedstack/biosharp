namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

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
        public long NumClusters { get; private set; }

        /** Byte representing a cluster failing filter(not a PF read), we test this exactly at
     * the moment but technically the standard  may be to check only lowest significant bit */
        private const byte FailedFilter = 0x00;

        /** Byte representing a cluster passing filter(a PF read), we test this exactly at
     * the moment but technically the standard  may be to check only lowest significant bit */
        private const byte PassedFilter = 0x01;

        /** The index of the current cluster within the file*/
        //private int _currentCluster;

        private byte[] _data = Array.Empty<byte>();

        private FilterFileReader(FileInfo file)
        {
            _bbIterator = File.Open(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        public static async Task<FilterFileReader> Create(FileInfo file, CancellationToken cancellationToken = default)
        {
            var instance = new FilterFileReader(file);
            // MMapBackedIteratorFactory.getByteIterator(HEADER_SIZE, file);
            byte[] headerBuf = new byte[HeaderSize]; //bbIterator.getHeaderBytes();
            var read = await instance._bbIterator.ReadAsync(headerBuf, cancellationToken).ConfigureAwait(false);
            if (read != HeaderSize)
            {
                throw new Exception("Invalid filter file");
            }

            for (var i = 0; i < 4; i++)
            {
                if (headerBuf[i] != 0)
                {
                    throw new Exception(
                        $"The first four bytes of a Filter File should be 0 but byte {i} was {headerBuf[i]} in file {file.FullName}");
                }
            }

            instance.Version = BitConverter.ToInt32(headerBuf.AsSpan(4, 4));
            if (instance.Version != ExpectedVersion)
            {
                throw new Exception(
                    $"Expected version is {ExpectedVersion} but version found was {instance.Version} in file {file.FullName}");
            }

            instance.NumClusters =
                BitConverter.ToUInt32(headerBuf.AsSpan(8, 4)); // UnsignedTypeUtil.uIntToLong(headerBuf.getInt());
            if (instance._bbIterator.Length != instance.NumClusters + HeaderSize)
            {
                throw new Exception($"Filter file size mismatch in file {file.FullName}");
            }

            //instance._currentCluster = 0;
            instance._data = new byte[instance._bbIterator.Length - HeaderSize];
            await instance._bbIterator.ReadAsync(instance._data, cancellationToken).ConfigureAwait(false);
            return instance;
        }

        /// <inheritdoc />
        public IEnumerator<bool> GetEnumerator()
        {
            return _data.Select(
                    (value,i) =>
                    {
                        return value switch
                        {
                            PassedFilter => true,
                            FailedFilter => false,
                            _ => throw new Exception($"Didn't recognized PF Byte (0x{value:X}) for element ({i})")
                        };
                    })
                .GetEnumerator();
            //while (_currentCluster < NumClusters)
            //{
            //    var value = _data[_currentCluster];
            //    _currentCluster += 1;
            //    yield return value switch
            //    {
            //        PassedFilter => true,
            //        FailedFilter => false,
            //        _ => throw new Exception($"Didn't recognized PF Byte (0x{value:X}) for element ({_currentCluster})")
            //    };
            //}
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
