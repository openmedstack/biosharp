namespace OpenMedStack.BioSharp.Io.Vcf
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Model;
    using SharpCompress.Archives.GZip;

    public interface IDisposableAsyncEnumerable<out T> : IAsyncDisposable, IAsyncEnumerable<T> { }

    public interface IHeaderedDisposableAsyncEnumerable<out THeader, out T> : IAsyncDisposable, IAsyncEnumerable<T>
    {
        public THeader Header { get; }
    }

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

    internal struct NoopDisposable : IDisposable
    {
        /// <inheritdoc />
        public void Dispose()
        {
        }
    }

    public class VcfFileReader
    {
        private readonly VariantReader _variantReader;
        private readonly VcfMetaReader _metaReader;

        public VcfFileReader(VariantReader variantReader, VcfMetaReader metaReader)
        {
            _variantReader = variantReader;
            _metaReader = metaReader;
        }

        public async Task<IHeaderedDisposableAsyncEnumerable<IVariantMetaInformation[], Variant>> Read(
            string path,
            CancellationToken cancellationToken = default)
        {
            if (Path.HasExtension(".gz"))
            {
                var zip = GZipArchive.Open(path);
                var stream = zip.Entries.First().OpenEntryStream();
                return await Read(zip, stream, cancellationToken).ConfigureAwait(false);
            }

            await using var file = File.OpenRead(path);
            return await Read(new NoopDisposable(), file, cancellationToken).ConfigureAwait(false);
        }

        public Task<IHeaderedDisposableAsyncEnumerable<IVariantMetaInformation[], Variant>> Read(
            Stream file,
            CancellationToken cancellationToken = default)
        {
            return Read(new NoopDisposable(), file, cancellationToken);
        }

        private async Task<IHeaderedDisposableAsyncEnumerable<IVariantMetaInformation[], Variant>> Read(
            IDisposable archive,
            Stream file,
            CancellationToken cancellationToken = default)
        {
            var headers = new List<IVariantMetaInformation>();
            var headerLength = 0;
            string residualText;
            using (var reader = new StreamReader(file, leaveOpen: true))
            {
                while (true)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line?.StartsWith("##") == true)
                    {
                        headerLength += line.Length + 1;
                        headers.Add(_metaReader.Read(line));
                    }
                    else if (line?.StartsWith("#CHROM") == true)
                    {
                        headerLength += line.Length + 1;
                        var buffer = new char[file.Position - headerLength];
                        await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                        residualText = new string(buffer);
                        break;
                    }
                    else
                    {
                        throw new Exception("Expected content header at this point.");
                    }
                }
            }

            return new HeaderedAsyncZipReader<IVariantMetaInformation[], Variant>(
                headers.ToArray(),
                archive,
                file,
                () => ReadVariants(residualText, file, cancellationToken));
        }

        private async IAsyncEnumerable<Variant> ReadVariants(
            string buffer,
            Stream stream,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var endsWithline = buffer[^1] != '\n';
            var lines = buffer.Split('\n', StringSplitOptions.TrimEntries);
            for (var i = 0; i < lines.Length - 1; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }

                if (!string.IsNullOrWhiteSpace(lines[i]))
                {
                    yield return await _variantReader.Read(lines[i]).ConfigureAwait(false);
                }
            }
            using var reader = new StreamReader(stream);
            while (!reader.EndOfStream)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }

                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (endsWithline)
                {
                    line = lines[^1] + line;
                    endsWithline = false;
                }
                if (line != null)
                {
                    yield return await _variantReader.Read(line).ConfigureAwait(false);
                }
            }

            if (endsWithline)
            {
                yield return await _variantReader.Read(lines[^1]).ConfigureAwait(false);
            }
        }
    }
}
