﻿namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
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
        private readonly Stream _filterFile;

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

        //private byte[] _data = Array.Empty<byte>();

        private FilterFileReader(FileInfo file)
        {
            _filterFile = File.Open(
                file.FullName,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                    Share = FileShare.Read
                });
        }

        public static async Task<FilterFileReader> Create(FileInfo file, CancellationToken cancellationToken = default)
        {
            var instance = new FilterFileReader(file);
            // MMapBackedIteratorFactory.getByteIterator(HEADER_SIZE, file);
            var headerBuf = new byte[HeaderSize]; //bbIterator.getHeaderBytes();
            var read = await instance._filterFile.ReadAsync(headerBuf, cancellationToken).ConfigureAwait(false);
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
                BitConverter.ToUInt32(headerBuf.AsSpan(8, 4));
            
            if (instance._filterFile.Length != instance.NumClusters + HeaderSize)
            {
                throw new Exception($"Filter file size mismatch in file {file.FullName}");
            }
            
            return instance;
        }

        /// <inheritdoc />
        public IEnumerator<bool> GetEnumerator()
        {
            int current;
            while((current =_filterFile.ReadByte()) != -1)
            {
                yield return current switch
                {
                    PassedFilter => true,
                    FailedFilter => false,
                    _ => throw new Exception($"Didn't recognized PF Byte (0x{current:X})")
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
