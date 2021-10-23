namespace OpenMedStack.BioSharp.Model
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Text;
    using Bcl;

    public struct Sequence : IEnumerable<BasePair>
    {
        private readonly Memory<byte> _data;
        private readonly Memory<byte> _qualities;

        internal Sequence(string id, Memory<byte> data, Memory<byte> qualities)
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
            return new Sequence(data.ToSequenceHeader(run), data.Bases, Array.ConvertAll(data.Qualities.ToArray(), b => (byte)(b + 33)));
        }

        public string Id { get; }

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
            for (int i = 0; i < _data.Length; i++)
            {
                yield return new BasePair(_data.Span[i], _qualities.Span[i]);
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
                   + Encoding.ASCII.GetString(_data.Span)
                   + Environment.NewLine
                   + Encoding.ASCII.GetString(_qualities.Span);
        }
    }
}
