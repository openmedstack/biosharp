namespace OpenMedStack.BioSharp.Model
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

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

            Header = SequenceHeader.Empty;
            Id = id;
            _data = data;
            _qualities = qualities;
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
            return string.Concat($"{Id}:{Length}",
                   Environment.NewLine,
                   new string(_data.Span),
                   Environment.NewLine,
                   new string(_qualities.Span));
        }
    }
}
