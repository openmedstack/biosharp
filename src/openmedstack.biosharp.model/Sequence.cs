namespace OpenMedStack.BioSharp.Model
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Text;

    public class Sequence : IEnumerable<BasePair>
    {
        private readonly byte[] _data;
        private readonly byte[] _qualities;

        internal Sequence(string id, byte[] data, byte[] qualities, bool isIndexed = false)
        {
            if (data.Length != qualities.Length)
            {
                throw new ArgumentException("Invalid data", nameof(qualities));
            }
            Id = id;
            IsIndexed = isIndexed;
            _data = data;
            _qualities = qualities;
        }

        public string Id { get; }

        public bool IsIndexed { get; }

        public int Length
        {
            get { return _data.Length; }
        }

        public char this[int index]
        {
            get { return (char)_data[index]; }
        }

        public IEnumerator<BasePair> GetEnumerator()
        {
            for (var i = 0; i < _qualities.Length; i++)
            {
                yield return new BasePair(_data[i], _qualities[i]);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public ReadOnlyMemory<byte> GetData()
        {
            return _data;
        }

        public ReadOnlyMemory<byte> GetQuality()
        {
            return _qualities;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{Id}:{Length}"
                   + Environment.NewLine
                   + Encoding.ASCII.GetString(_data)
                   + Environment.NewLine
                   + Encoding.ASCII.GetString(_qualities);
        }
    }
}
