//namespace OpenMedStack.BioSharp.Io.Bcl
//{
//    using System;
//    using System.Collections.Concurrent;
//    using System.Collections.Generic;
//    using System.IO;
//    using System.Linq;
//    using System.Threading;
//    using System.Threading.Tasks;
//    using FastQ;
//    using Microsoft.Extensions.Logging;
//    using Model;
//    using Model.Bcl;

//    public class DemultiplexFastQWriter : IAsyncDisposable
//    {
//        private readonly Func<ClusterData, string> _pathResolver;
//        private readonly Run _runInfo;
//        private readonly ILogger _logger;
//        private readonly ConcurrentDictionary<string, FastQWriter> _files = new();

//        public DemultiplexFastQWriter(Func<ClusterData, string> pathResolver, Run runInfo, ILogger logger)
//        {
//            _pathResolver = pathResolver;
//            _runInfo = runInfo;
//            _logger = logger;
//        }

//        public async Task WriteDemultiplexed(
//            IAsyncEnumerable<ClusterData> input,
//            CancellationToken cancellationToken = default)
//        {
//            async Task WritetoFastQ(IEnumerable<ClusterData> clusterData)
//            {
//                _logger.LogInformation("Writing to {0} on thread {1}", clusterData.Key, Environment.CurrentManagedThreadId);
//                var writer = _files.GetOrAdd(
//                    clusterData.Key,
//                    p =>
//                    {
//                        var directoryName = Path.GetDirectoryName(p)!;
//                        if (!Directory.Exists(directoryName))
//                        {
//                            Directory.CreateDirectory(directoryName);
//                        }

//                        return new FastQWriter(_logger, File.Open(p, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None));
//File.Open(
//    filePath,
//    new FileStreamOptions
//    {
//        Access = FileAccess.Read,
//        Mode = FileMode.Open,
//        Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
//        Share = FileShare.Read
//    })
//                    });
//                await writer.Write(clusterData.Select(x => new Sequence(x, _runInfo)), cancellationToken).ConfigureAwait(false);
//                if (clusterData is IDisposable disposable)
//                {
//                    disposable.Dispose();
//                }

//                _logger.LogInformation("Finished writing to {0} on thread {1}", clusterData.Key, Environment.CurrentManagedThreadId);
//            }

//            var tasks = new List<Task>();
//            await foreach (var clusterData in input.ConfigureAwait(false))//.GroupInto(_pathResolver, cancellationToken).ConfigureAwait(false))
//            {
//                await Task.Yield();
//                var task = WritetoFastQ(clusterData);
//                tasks.Add(task);
//            }

//            await Task.WhenAll(tasks).ConfigureAwait(false);

//            foreach (var t in tasks)
//            {
//                t.Dispose();
//            }
//            tasks.Clear();
//        }

//        /// <inheritdoc />
//        public async ValueTask DisposeAsync()
//        {
//            foreach (var fastQWriter in _files.Values)
//            {
//                await fastQWriter.DisposeAsync().ConfigureAwait(false);
//            }
//            GC.SuppressFinalize(this);
//        }
//    }
//}
