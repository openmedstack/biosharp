namespace OpenMedStack.BioSharp.Io.Vcf
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Model;
    using OpenMedStack.BioSharp.Model.Vcf;

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
                var fileContent = File.Open(
                    path,
                    new FileStreamOptions
                    {
                        Access = FileAccess.Read,
                        Mode = FileMode.Open,
                        Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                        Share = FileShare.Read
                    }); ;
                //var zip = new ZipArchive(fileContent);
                var stream = new GZipStream(fileContent, CompressionMode.Decompress); //zip.Entries.First().Open();
                return await Read(stream, cancellationToken).ConfigureAwait(false);
            }

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
            return await Read(file, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IHeaderedDisposableAsyncEnumerable<IVariantMetaInformation[], VcfVariant>> Read(
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
                        var filePosition = file is GZipStream g ? g.BaseStream.Position : file.Position;
                        var buffer = new char[filePosition - headerLength];
                        var read = 0;
                        while (read < buffer.Length)
                        {
                            var r = await reader.ReadAsync(buffer, read, buffer.Length - read).ConfigureAwait(false);
                            read += r;
                            if (r <= 0)
                            {
                                break;
                            }
                        }

                        residualText = new string(buffer).TrimEnd();
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
                new NoopDisposable(),
                file,
                () => ReadVariants(residualText, file, cancellationToken));
        }

        private static async IAsyncEnumerable<VcfVariant> ReadVariants(
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
