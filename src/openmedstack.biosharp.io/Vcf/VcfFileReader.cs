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

    public class VcfFileReader
    {
        private readonly VcfMetaReader _metaReader;

        public VcfFileReader(VcfMetaReader metaReader)
        {
            _metaReader = metaReader;
        }

        public async Task<IHeaderedDisposableAsyncEnumerable<IVariantMetaInformation[], VcfVariant>> Read(
            string path,
            CancellationToken cancellationToken = default)
        {
            if (Path.HasExtension(".gz"))
            {
                var zip = GZipArchive.Open(path);
                var stream = zip.Entries.First().OpenEntryStream();
                return await Read(zip, stream, cancellationToken).ConfigureAwait(false);
            }

            var file = File.OpenRead(path);
            await using var _ = file.ConfigureAwait(false);
            return await Read(new NoopDisposable(), file, cancellationToken).ConfigureAwait(false);
        }

        public Task<IHeaderedDisposableAsyncEnumerable<IVariantMetaInformation[], VcfVariant>> Read(
            Stream file,
            CancellationToken cancellationToken = default)
        {
            return Read(new NoopDisposable(), file, cancellationToken);
        }

        private async Task<IHeaderedDisposableAsyncEnumerable<IVariantMetaInformation[], VcfVariant>> Read(
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

            return new HeaderedAsyncZipReader<IVariantMetaInformation[], VcfVariant>(
                headers.ToArray(),
                archive,
                file,
                () => ReadVariants(residualText, file, cancellationToken));
        }

        private async IAsyncEnumerable<VcfVariant> ReadVariants(
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
                    yield return VcfVariant.Parse(lines[i]);
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
                    yield return VcfVariant.Parse(line);
                }
            }

            if (endsWithline)
            {
                yield return VcfVariant.Parse(lines[^1]);
            }
        }
    }
}
