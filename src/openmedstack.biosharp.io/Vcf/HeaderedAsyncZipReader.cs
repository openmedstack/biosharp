namespace OpenMedStack.BioSharp.Io.Vcf
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    internal class HeaderedAsyncZipReader<THeader, T> : AsyncZipReader<T>, IHeaderedDisposableAsyncEnumerable<THeader, T>
    {
        /// <inheritdoc />
        public HeaderedAsyncZipReader(THeader header, IDisposable archive, Stream stream, Func<IAsyncEnumerable<T>> asyncCreator)
            : base(archive, stream, asyncCreator)
        {
            Header = header;
        }

        /// <inheritdoc />
        public THeader Header { get; }
    }
}