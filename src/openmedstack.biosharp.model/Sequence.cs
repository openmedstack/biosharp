namespace OpenMedStack.BioSharp.Model
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Bcl;

    public class Sequence : IEnumerable<BasePair>
    {
        private readonly ReadOnlyMemory<char> _data;
        private readonly ReadOnlyMemory<char> _qualities;

        internal Sequence(SequenceHeader header, ReadOnlyMemory<char> data, ReadOnlyMemory<char> qualities)
        : this(header.ToString(), data, qualities)
        {
            Header = header;
        }

        internal Sequence(string id, ReadOnlyMemory<char> data, ReadOnlyMemory<char> qualities)
        {
            if (data.Length != qualities.Length)
            {
                throw new ArgumentException("Invalid data", nameof(qualities));
            }

            Id = id;
            _data = data;
            _qualities = qualities;
        }

        public static Sequence FromCluster(ClusterData data, Run run)
        {
            var bytes = Array.ConvertAll(data.Qualities.ToArray(), b => (char)(b + 33));
            var header = new SequenceHeader(
                data.Barcode,
                run.Instrument,
                run.Number,
                run.Flowcell,
                data.Lane,
                data.Tile,
                data.Position,
                data.PairedEndRead,
                data.Filtered,
                data.Direction);
            return new Sequence(header, data.Bases, bytes);
        }

        public string Id { get; }

        public SequenceHeader Header { get; }

        public int Length
        {
            get { return _data.Length; }
        }

        public char this[int index]
        {
            get { return (char)_data.Span[index]; }
        }

        public IEnumerator<BasePair> GetEnumerator()
        {
            for (var i = 0; i < _data.Length; i++)
            {
                yield return new BasePair(_data.Span[i], _qualities.Span[i]);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public ReadOnlyMemory<char> GetData()
        {
            return _data;
        }

        public ReadOnlyMemory<char> GetQuality()
        {
            return _qualities;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{Id}:{Length}"
                   + Environment.NewLine
                   + new string(_data.Span)
                   + Environment.NewLine
                   + new string(_qualities.Span);
        }
    }
}
