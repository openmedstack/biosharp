﻿namespace OpenMedStack.BioSharp.Io.FastQ
{
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Model;

    public class FastQReader
    {
        private readonly ILogger _logger;

        public FastQReader(ILogger logger)
        {
            _logger = logger;
        }

        public async IAsyncEnumerable<Sequence> Read(string path, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var file = File.Open(
                path,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                    Share = FileShare.Read
                });
            await using var _ = file.ConfigureAwait(false);
            var gzip = new GZipStream(file, CompressionMode.Decompress, false);
            await using var __ = gzip.ConfigureAwait(false);
            using var reader = new StreamReader(gzip);
            while (file.Position < file.Length)
            {
                _logger.LogInformation("Read {file} to {position}/{length}", file, file.Position, file.Length);
                cancellationToken.ThrowIfCancellationRequested();
                var id = await reader.ReadLineAsync().ConfigureAwait(false);
                var letters = await reader.ReadLineAsync().ConfigureAwait(false);

                var q = await reader.ReadLineAsync().ConfigureAwait(false);
                if (q == "+")
                {
                    q = await reader.ReadLineAsync().ConfigureAwait(false);
                }

                yield return new Sequence(id![1..], letters!.ToCharArray(), q!.ToCharArray());
            }
        }
    }
}
