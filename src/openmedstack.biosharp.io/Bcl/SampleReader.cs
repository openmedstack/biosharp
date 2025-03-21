﻿namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Model;
    using Model.Bcl;

    public class SampleReader : IAsyncDisposable
    {
        private readonly BclReader _reader;
        private readonly Run _run;
        private readonly int _sample;
        private readonly ILocationReader _positionReader;
        private readonly IEnumerable<bool> _filter;

        public SampleReader(
            Run run,
            int lane,
            int sample,
            BclReader reader,
            ILocationReader positionReader,
            IEnumerable<bool> filter)
        {
            Lane = lane;
            _run = run;
            _sample = sample;
            _positionReader = positionReader;
            _filter = filter;
            _reader = reader;
        }

        public int Lane { get; }

        public async IAsyncEnumerable<Sequence> ReadBclData(
            IQualityTrimmer qualityTrimmer,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var positionalEnumerator = _positionReader.GetAsyncEnumerator(cancellationToken);
            await using var positionEnumerator = positionalEnumerator.ConfigureAwait(false);
            var dataReader = (IAsyncEnumerable<ReadData[]>)_reader;
            using var filter = _filter.GetEnumerator();

            await foreach (var data in dataReader.Where(x => qualityTrimmer.Trim(x)).ConfigureAwait(false))
            {
                if (!await positionalEnumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    throw new Exception("Could not read position for sequence read");
                }

                if (!filter.MoveNext())
                {
                    throw new Exception("Could not read filter for cluster");
                }

                var barcode = _sample.ToString();
                var barcodes = data.Where(x => x.Type == ReadType.B)
                    .Select(x => new string(x.Bases.Span))
                    .ToArray();

                var pairedEndRead = barcodes.Length > 1;

                var filtered = filter.Current;
                //var trim = (await qualityTrimmer.Trim(data).ConfigureAwait(false));
                for (var i = 0; i < data.Length; i++)
                {
                    var r = data[i];
                    var b = barcodes.Length > 0 ? i == 1 || barcodes.Length == 1 ? barcodes[0] : barcodes[1] : barcode;
                    var forwardLength = data.Length / 2;
                    yield return new Sequence(
                        new SequenceHeader(
                            b,
                            _run.Instrument,
                            _run.Number,
                            _run.Flowcell,
                            Lane,
                            _reader.Tile,
                            positionalEnumerator.Current,
                            pairedEndRead,
                            filtered,
                            pairedEndRead && i > forwardLength ? ReadDirection.Reverse : ReadDirection.Forward,
                            r.Type),
                        r.Bases,
                     Array.ConvertAll(r.Qualities.ToArray(), c => (char)(c + 33)));
                }
            }

            filter.Dispose();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _reader.DisposeAsync().ConfigureAwait(false);
            await _positionReader.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }
}
