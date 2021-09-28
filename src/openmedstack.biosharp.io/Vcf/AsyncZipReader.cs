namespace OpenMedStack.BioSharp.Io.Vcf
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    internal class AsyncZipReader<T> : IDisposableAsyncEnumerable<T>
    {
        private readonly IDisposable _archive;
        private readonly Stream _stream;
        private readonly Func<IAsyncEnumerable<T>> _asyncCreator;
        private bool _enumerableCreated;

        public AsyncZipReader(IDisposable archive, Stream stream, Func<IAsyncEnumerable<T>> asyncCreator)
        {
            _archive = archive;
            _stream = stream;
            _asyncCreator = asyncCreator;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            _archive.Dispose();
            await _stream.DisposeAsync().ConfigureAwait(false);
        }

        /// <inheritdoc />
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = new())
        {
            if (_enumerableCreated)
            {
                throw new InvalidOperationException("Cannot create second enumerable");
            }

            _enumerableCreated = true;
            return _asyncCreator().GetAsyncEnumerator(cancellationToken);
        }
    }
}