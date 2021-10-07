namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using FastQ;
    using Microsoft.Extensions.Logging.Abstractions;
    using Model;
    using Model.Bcl;

    public class DemultiplexFastQWriter : IAsyncDisposable
    {
        private readonly Func<Run, string, string> _pathResolver;
        private readonly Run _runInfo;
        private readonly ConcurrentDictionary<string, FastQWriter> _files = new();

        public DemultiplexFastQWriter(Func<Run, string, string> pathResolver, Run runInfo)
        {
            _pathResolver = pathResolver;
            _runInfo = runInfo;
        }

        public async Task WriteDemultiplexed(
            IAsyncEnumerable<(string, Sequence[])> input,
            CancellationToken cancellationToken = default)
        {
            await foreach (var (index, sequences) in input.ConfigureAwait(false))
            {
                var path = _pathResolver(_runInfo, index);
                var writer = _files.GetOrAdd(
                    path,
                    p => new FastQWriter(
                        NullLogger.Instance,
                        File.Open(p, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None)));
                await writer.Write(sequences.Where(s => !s.IsIndexed), cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            foreach (var fastQWriter in _files.Values)
            {
                await fastQWriter.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
