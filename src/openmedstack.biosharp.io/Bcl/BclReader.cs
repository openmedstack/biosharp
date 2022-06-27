/* The code in this file is migrated from the Java code in the Picard project (https://github.com/broadinstitute/picard).

The code is released under MIT license.*/

namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Model;
    using Model.Bcl;

    /**
 * BCL FileInfos are base call and quality score binary files containing a (base,quality) pair for successive clusters.
 * The file is structured as followed:
 * Bytes 1-4 : unsigned int numClusters
 * Bytes 5-numClusters + 5 : 1 byte base/quality score
 * <p/>
 * The base/quality scores are organized as follows (with one exception, SEE BELOW):
 * The right 2 most bits (these are the LEAST significant bits) indicate the base, where
 * A=00(0x00), C=01(0x01), G=10(0x02), and T=11(0x03)
 * <p/>
 * The remaining bytes compose the quality score which is an unsigned int.
 * <p/>
 * EXCEPTION: If a byte is entirely 0 (e.g. byteRead == 0) then it is a no call, the base
 * becomes '.' and the Quality becomes 2, the default illumina masking value
 * <p/>
 * (E.g. if we get a value in binary of 10001011 it gets transformed as follows:
 * <p/>
 * Value read: 10001011(0x8B)
 * <p/>
 * Quality     Base
 * <p/>
 * 100010      11
 * 00100010    0x03
 * 0x22        T
 * 34          T
 * <p/>
 * So the output base/quality will be a (T/34)
 */
    public class BclReader : IAsyncDisposable, IAsyncEnumerable<ReadData[]>
    {
        private readonly ILogger _logger;
        private const int DefaultQueueSize = 256; //1024 * 1024;
        private const int EamssM2GeThreshold = 30;
        private const int EamssS1LtThreshold = 15; //was 15
        private const char MaskingQuality = (char)0x02;

        /* Array of base values and quality values that are used to decode values from BCLs efficiently. */
        private static readonly char[] BclBaseLookup = new char[256];
        private static readonly char[] BclQualLookup = new char[256];

        private const byte BaseMask = 0x0003;
        private static readonly char[] BaseLookup = { 'A', 'C', 'G', 'T' };
        private const char NoCallBase = 'N';
        private readonly BclQualityEvaluationStrategy _bclQualityEvaluationStrategy;
        private readonly bool _applyEamss;

        static BclReader()
        {
            BclBaseLookup[0] = NoCallBase;
            BclQualLookup[0] = BclQualityEvaluationStrategy.IlluminaAllegedMinimumQuality;

            foreach (var x in Enumerable.Range(1, 255))
            {
                var i = (byte)x;
                // TODO: If we can remove the use of BclQualityEvaluationStrategy then in the lookup we
                // TODO: can just set the QUAL to max(2, (i >>> 2)) instead.
                BclBaseLookup[i] = BaseLookup[i & BaseMask];
                BclQualLookup[i] = (char)Math.Max(2, i >> 2);
            }
        }

        private static readonly int HeaderSize = 4;
        private readonly TileIndexRecord _tileIndexRecord;
        private readonly int _queueSize;

        public static async Task<BclReader> Create(
            IReadOnlyList<FileInfo> bclsForOneTile,
            IEnumerable<Read> reads,
            TileIndexRecord tileIndexRecord,
            BclQualityEvaluationStrategy bclQualityEvaluationStrategy,
            ILogger logger,
            IDictionary<string, IBclIndexReader>? indexReaders = null,
            bool applyEamss = false,
            int queueSize = DefaultQueueSize)
        {
            var reader = new BclReader(
                reads,
                tileIndexRecord,
                bclQualityEvaluationStrategy,
                applyEamss,
                queueSize,
                logger);
            for (var i = 0; i < reader.Cycles; ++i)
            {
                var bclFileInfo = bclsForOneTile[i];
                if (!File.Exists(bclFileInfo.FullName))
                {
                    await reader.DisposeAsync().ConfigureAwait(false);
                    throw new IOException($"Could not find BCL file for cycle {i}");
                }

                var offset = indexReaders == null || indexReaders.Count == 0
                    ? new BlockOffsetRecord(0, 4)
                    : await indexReaders[bclFileInfo.FullName]
                        .Get(tileIndexRecord.ZeroBasedTileNumber).ConfigureAwait(false);
                reader.Streams[i] = await Open(bclFileInfo, offset).ConfigureAwait(false);
            }
            return reader;
        }

        internal static async Task<BclReader> Create(
            FileInfo bclFileInfo,
            TileIndexRecord tileIndexRecord,
            BclQualityEvaluationStrategy bclQualityEvaluationStrategy,
            ILogger logger,
            bool applyEamss = false,
            int queueSize = DefaultQueueSize)
        {
            var reader = new BclReader(
                new[] { new Read { IsIndexedRead = "N", NumCycles = 1, Number = 1, Type = ReadType.T } },
                tileIndexRecord,
                bclQualityEvaluationStrategy,
                applyEamss,
                queueSize,
                logger);
            var byteBuffer = new byte[HeaderSize];
            var stream = await Open(bclFileInfo, new BlockOffsetRecord(0, 0)).ConfigureAwait(false);
            var read = await stream.ReadAsync(byteBuffer.AsMemory()).ConfigureAwait(false);

            if (read != HeaderSize)
            {
                throw new IOException($"BCL {bclFileInfo.FullName} has invalid header structure.");
            }

            reader.NumClustersPerCycle[0] = BitConverter.ToInt32(byteBuffer);

            reader.Streams[0] = stream;
            return reader;
        }

        private BclReader(
            IEnumerable<Read> reads,
            TileIndexRecord tileIndexRecord,
            BclQualityEvaluationStrategy bclQualityEvaluationStrategy,
            bool applyEamss,
            int queueSize,
            ILogger logger)
        {
            _tileIndexRecord = tileIndexRecord;
            _queueSize = queueSize;
            _logger = logger;
            var r = reads.OrderBy(r => r.Number).ToArray();
            OutputLengths = r.Select(x => x.NumCycles).ToArray();
            ReadTypes = r.Select(x => x.Type).ToArray();
            NumReads = r.Length;
            _bclQualityEvaluationStrategy = bclQualityEvaluationStrategy;
            _applyEamss = applyEamss;

            var cycles = OutputLengths.Sum();

            Cycles = cycles;
            Streams = new Stream[cycles];
            //StreamFileInfos = new FileInfo[cycles];
            NumClustersPerCycle = new int[cycles];
        }

        private Stream[] Streams { get; }
        private int[] OutputLengths { get; }
        private ReadType[] ReadTypes { get; }
        private int NumReads { get; }
        public int[] NumClustersPerCycle { get; }
        public int Tile => _tileIndexRecord.Tile;
        private int Cycles { get; }

        private static async Task<Stream> Open(FileInfo file, BlockOffsetRecord offset)
        {
            await Task.Yield();
            var filePath = file.FullName;

            var isGzip = Path.GetExtension(filePath).Equals(".gz", StringComparison.OrdinalIgnoreCase);
            var isBgzf = Path.GetExtension(filePath).Equals(".bgzf", StringComparison.OrdinalIgnoreCase);
            var sourceFile = File.Open(
                filePath,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                    Share = FileShare.Read
                });
            sourceFile.Seek((long)offset.BlockAddress, SeekOrigin.Begin);
            /*await using*/
            Stream stream = isGzip || isBgzf ? new GZipStream(sourceFile, CompressionMode.Decompress) : sourceFile;

            var arrayPool = ArrayPool<byte>.Shared;
            var discard = arrayPool.Rent(offset.BlockOffset);
            var read = stream.Read(discard.AsSpan(0, offset.BlockOffset));
            if (read != offset.BlockOffset)
            {
                throw new Exception("Could not go to block offset");
            }
            arrayPool.Return(discard);
            //var ms = new MemoryStream();
            //await stream.CopyToAsync(ms).ConfigureAwait(false);
            //ms.Position = 0;
            //return ms;
            return stream;
        }

        private async IAsyncEnumerable<ReadData[]> Read([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Start reading {clusters} clusters from tile {tile}",
                _tileIndexRecord.NumClustersInTile == int.MaxValue ? "all" : _tileIndexRecord.NumClustersInTile,
                _tileIndexRecord.Tile);

            var queue = ArrayPool<byte>.Shared.Rent(_queueSize);
            var bclDataArray = ArrayPool<BclData>.Shared.Rent(_queueSize);
            var readIndex = 0;
            while (readIndex < _tileIndexRecord.NumClustersInTile && !cancellationToken.IsCancellationRequested)
            {
                var buffer = queue.AsMemory(0, _queueSize);
                // See how many clusters we can read and then make BclData objects for them
                var clustersRead = await Streams[0].FillBuffer(buffer, true, cancellationToken).ConfigureAwait(false);
                if (clustersRead.Length == 0)
                {
                    break;
                }

                for (var i = 0; i < clustersRead.Length; ++i)
                {
                    bclDataArray[i] = new BclData(OutputLengths);
                }

                var bclDatas = bclDataArray.AsMemory(0, clustersRead.Length);
                // Process the data from the first cycle since we had to read it to know how many clusters we'd get
                //UpdateClusterBclDatas(bclDatas.Span, 0, 0, buffer[..clustersRead].Span);
                UpdateClusterBclDatas(bclDatas.Span, 0, 0, clustersRead.Span); //buffer[..clustersRead].Span);
                var totalCycleCount = 1;

                for (var read = 0; read < NumReads; ++read)
                {
                    var readLen = OutputLengths[read];
                    var firstCycle = read == 0 ? 1 : 0; // For the first read we already did the first cycle above

                    for (var cycle = firstCycle; cycle < readLen; ++cycle)
                    {
                        //using var task = ReadAndUpdateData(buffer[..clustersRead], bclDatas, read, cycle, totalCycleCount, cancellationToken);
                        await ReadAndUpdateData(clustersRead, bclDatas, read, cycle, totalCycleCount, cancellationToken).ConfigureAwait(false);
                        totalCycleCount++;
                    }
                }

                foreach (var bclData in bclDataArray.Take(clustersRead.Length))
                {
                    if (_applyEamss)
                    {
                        for (var i = 0; i < bclData.Bases.Length; i++)
                        {
                            RunEamssForReadInPlace(bclData.Bases[i], bclData.Qualities[i]);
                        }
                    }

                    var ri = readIndex++;
                    var readData = new ReadData[bclData.Bases.Length];
                    for (var i = 0; i < bclData.Bases.Length; i++)
                    {
                        readData[i] = new ReadData(_tileIndexRecord.Tile, ReadTypes[i], bclData.Bases[i], bclData.Qualities[i], ri);
                    }
                    yield return readData;
                    if (readIndex == _tileIndexRecord.NumClustersInTile)
                    {
                        break;
                    }
                }
            }
            ArrayPool<byte>.Shared.Return(queue);
            ArrayPool<BclData>.Shared.Return(bclDataArray);

            _logger.LogInformation(
                "Finished reading {readIndex} clusters from tile {tile} out of {numberOfClusters}",
                readIndex,
                _tileIndexRecord.Tile,
                _tileIndexRecord.NumClustersInTile == int.MaxValue
                    ? "whole file"
                    : _tileIndexRecord.NumClustersInTile);
        }

        private async Task ReadAndUpdateData(
            Memory<byte> buffer,
            Memory<BclData> bclDatas,
            int read,
            int cycle,
            int totalCycleCount,
            CancellationToken cancellationToken)
        {
            var mem = await Streams[totalCycleCount].FillBuffer(buffer, cancellationToken).ConfigureAwait(false);

            UpdateClusterBclDatas(bclDatas.Span, read, cycle, mem.Span);
        }

        /** Inserts the bases and quals at `cycle` of `read` in all of the bclDatas, using data in this.buffer. */
        private void UpdateClusterBclDatas(Span<BclData> bclDatas, int read, int cycle, Span<byte> buffer)
        {
            var numClusters = bclDatas.Length;
            for (var dataIdx = 0; dataIdx < numClusters; ++dataIdx)
            {
                var data = bclDatas[dataIdx];
                var b = (uint)buffer[dataIdx];
                data.Bases[read][cycle] = BclBaseLookup[b];
                data.Qualities[read][cycle] = _bclQualityEvaluationStrategy.ReviseAndConditionallyLogQuality(BclQualLookup[b]);
            }
        }

        /**
         * EAMSS is an Illumina Developed Algorithm for detecting reads whose quality has deteriorated towards
         * their end and revising the quality to the masking quality (2) if this is the case.  This algorithm
         * works as follows (with one exception):
         * <p/>
         * Start at the end (high indices, at the right below) of the read and calculate an EAMSS tally at each
         * location as follow:
         * if(quality[i] < 15) tally += 1
         * if(quality[i] >= 15 and < 30) tally = tally
         * if(quality[i] >= 30) tally -= 2
         * <p/>
         * <p/>
         * For each location, keep track of this tally (e.g.)
         * Read Starts at <- this end
         * Cycle:       1  2  3  4  5  6  7  8  9
         * Bases:       A  C  T  G  G  G  T  C  A
         * Qualities:   32 32 16 15 8  10 32 2  2
         * Cycle Score: -2 -2 0  0  1  1  -2 1  1           //The EAMSS Score determined for this cycle alone
         * EAMSS TALLY: 0  0  2  2  2  1  0  2  1
         * X - Earliest instance of Max-Score
         * <p/>
         * You must keep track of the maximum EAMSS tally (in this case 2) and the earliest(lowest) cycle at which
         * it occurs.  If and only if, the max EAMSS tally >= 1 then from there until the end(highest cycle) of the
         * read reassign these qualities as 2 (the masking quality).  The output qualities would therefore be
         * transformed from:
         * <p/>
         * Original Qualities: 32 32 16 15 8  10 32 2  2    to
         * Final    Qualities: 32 32 2  2  2  2  2  2  2
         * X - Earliest instance of max-tally/end of masking
         * <p/>
         * IMPORTANT:
         * The one exception is: If the max EAMSS Tally is preceded by a long string of G basecalls (10 or more, with a single basecall exception per10 bases)
         * then the masking continues to the beginning of that string of G's. E.g.:
         * <p/>
         * Cycle:       1  2  3  4  5  6  7  8   9  10 11 12 13 14 15 16 17 18
         * Bases:       C  T  A  C  A  G  A  G   G  G  G  G  G  G  G  C  A  T
         * Qualities:   30 22 26 27 28 30 7  34  20 19 38 15 32 32 10 4  2  5
         * Cycle Score: -2  0  0  0  0 -2 1  -2  0  0  -2 0  -2 -2  1 1  1  1
         * EAMSS TALLY: -2 -5 -5 -5 -5 -5 -3 -4 -2 -2  -2 0   0  2  4 3  2  1
         * X- Earliest instance of Max-Tally
         * <p/>
         * Resulting Transformation:
         * Bases:                C  T  A  C  A  G  A   G   G  G  G  G  G  G  G  C  A  T
         * Original Qualities:   30 22 26 27 28 30 7  34  20 19 38 15 32 32 10  4  2  5
         * Final    Qualities:   30 22 26 27 28  2 2   2   2  2  2  2  2  2  2  2  2  2
         * X- Earliest instance of Max-Tally
         * X - Start of EAMSS masking due to G-Run
         * <p/>
         * To further clarify the exception rule here are a few examples:
         * A C G A C G G G G G G G G G G G G G G G G G G G G A C T
         * X - Earliest instance of Max-Tally
         * X - Start of EAMSS masking (with a two base call jump because we have 20 bases in the run already)
         * <p/>
         * T T G G A G G G G G G G G G G G G G G G G G G A G A C T
         * X - Earliest instance of Max-Tally
         * X - We can skip this A as well as the earlier A because we have 20 or more bases in the run already
         * X - Start of EAMSS masking (with a two base call jump because we have 20 bases in the run)
         * <p/>
         * T T G G G A A G G G G G G G G G G G G G G G G G G T T A T
         * X - Earliest instance of Max-Tally
         * X X - WE can skip these bases because the first A counts as the first skip and as far as the length of the string of G's is
         * concerned, these are both counted like G's
         * X - This A is the 20th base in the string of G's and therefore can be skipped
         * X - Note that the A's previous to the G's are only included because there are G's further on that are within the number
         * of allowable exceptions away (i.e. 2 in this instance), if there were NO G's after the A's you CANNOT count the A's
         * as part of the G strings (even if no exceptions have previously occured) In other words, the end of the string of G's
         * MUST end in a G not an "exception"
         * <p/>
         * However, if the max-tally occurs to the right of the run of Gs then this is still part of the string of G's but does count towards
         * the number of exceptions allowable
         * (e.g.)
         * T T G G G G G G G G G G A C G
         * X - Earliest instance of Max-tally
         * The first index CAN be considered as an exception, the above would be masked to
         * the following point:
         * T T G G G G G G G G G G A C G
         * X - End of EAMSS masking due to G-Run
         * <p/>
         * To sum up the points, a string of G's CAN START with an exception but CANNOT END in an exception.
         *
         * @param bases     Bases for a single read in the cluster ( not the entire cluster )
         * @param qualities Qualities for a single read in the cluster ( not the entire cluster )
         */
        private static void RunEamssForReadInPlace(Span<char> bases, Span<char> qualities)
        {
            var eamssTally = 0;
            var maxTally = int.MinValue;
            var indexOfMax = -1;

            for (var i = bases.Length - 1; i >= 0; i--)
            {
                var quality = 0xff & qualities[i];

                if (quality >= EamssM2GeThreshold)
                {
                    eamssTally -= 2;
                }
                else if (quality < EamssS1LtThreshold)
                {
                    eamssTally += 1;
                }

                if (eamssTally >= maxTally)
                {
                    indexOfMax = i;
                    maxTally = eamssTally;
                }
            }

            if (maxTally >= 1)
            {
                var numGs = 0;
                var exceptions = 0;

                for (var i = indexOfMax; i >= 0; i--)
                {
                    if (bases[i] == 'G')
                    {
                        ++numGs;
                    }
                    else
                    {
                        var skip = SkipBy(i, numGs, exceptions, bases);
                        if (skip > -1)
                        {
                            exceptions += skip;
                            numGs += skip;
                            i -= skip - 1;
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                if (numGs >= 10)
                {
                    indexOfMax = indexOfMax + 1 - numGs;
                }

                for (var i = indexOfMax; i < qualities.Length; i++)
                {
                    qualities[i] = MaskingQuality;
                }
            }
        }

        /**
         * Determine whether or not the base at index is part of a skippable section in a run of G's, if so
         * return the number of bases that the section is composed of.
         *
         * @param index          Current index, which should be the index of a non-'G' base
         * @param numGs          The number of bases in the current string of G's for this read
         * @param prevExceptions The number of exceptions previously detected in this string by this method
         * @param bases          The bases of this read
         * @return If we have not reached our exception limit (1/every 10bases) and a G is within exceptionLimit(numGs/10)
         * indices before the current index then return index - (index of next g), else return null  Null indicates this is
         * NOT a skippable region, if we run into index 0 without finding a g then NULL is also returned
         */
        private static int SkipBy(int index, int numGs, int prevExceptions, Span<char> bases)
        {
            var skip = -1;
            for (var backup = 1; backup <= index; backup++)
            {
                var exceptionLimit = Math.Max((numGs + backup) / 10, 1);
                if (prevExceptions + backup > exceptionLimit)
                {
                    break;
                }
                if (bases[index - backup] == 'G')
                {
                    skip = backup;
                    break;
                }
            }

            return skip;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await Task.WhenAll(Streams.Select(x => x.DisposeAsync().AsTask().ContinueWith(t =>
            {
                if (t.Exception is not null)
                {
                    _logger.LogError(t.Exception, "{msg}", t.Exception.Message);
                    foreach (var innerException in t.Exception.InnerExceptions)
                    {
                        _logger.LogError(innerException, "{msg}", innerException.Message);
                    }
                }
            }))).ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc />
        public IAsyncEnumerator<ReadData[]> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return Read(cancellationToken).GetAsyncEnumerator(cancellationToken);
        }

        /// <summary>
        /// A class that holds the <see cref="BclData"/> provided by this parser.
        /// One BclData object is returned to IlluminaDataProvider per cluster and each first level array in bases and qualities represents a single read in that cluster.
        /// </summary>
        private class BclData //: IBaseData, IQualityData
        {
            public BclData(int[] outputLengths)
            {
                Bases = new char[outputLengths.Length][];
                Qualities = new char[outputLengths.Length][];

                for (var i = 0; i < outputLengths.Length; i++)
                {
                    Bases[i] = new char[outputLengths[i]];
                    Qualities[i] = new char[outputLengths[i]];
                }
            }

            public char[][] Bases { get; }

            public char[][] Qualities { get; }
        }

    }
}
