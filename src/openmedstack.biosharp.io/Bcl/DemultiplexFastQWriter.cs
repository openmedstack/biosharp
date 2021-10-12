namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using FastQ;
    using Microsoft.Extensions.Logging;
    using Model;
    using Model.Bcl;

    public class DemultiplexFastQWriter : IAsyncDisposable
    {
        private readonly Func<ClusterData, string> _pathResolver;
        private readonly Run _runInfo;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, FastQWriter> _files = new();

        public DemultiplexFastQWriter(Func<ClusterData, string> pathResolver, Run runInfo, ILogger logger)
        {
            _pathResolver = pathResolver;
            _runInfo = runInfo;
            _logger = logger;
        }

        public async Task WriteDemultiplexed(
            IAsyncEnumerable<ClusterData> input,
            CancellationToken cancellationToken = default)
        {
            await foreach (var clusterData in input.ConfigureAwait(false))
            {
                var path = _pathResolver(clusterData);
                var writer = _files.GetOrAdd(
                    path,
                    p =>
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                        return new FastQWriter(
                            _logger,
                            File.Open(p, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None));
                    });
                await writer.Write(new Sequence(clusterData, _runInfo), cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            foreach (var fastQWriter in _files.Values)
            {
                await fastQWriter.DisposeAsync().ConfigureAwait(false);
            }
            GC.SuppressFinalize(this);
        }
    }
}
