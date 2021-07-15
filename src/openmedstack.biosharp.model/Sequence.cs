namespace OpenMedStack.BioSharp.Model
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    
    public class Sequence : IEnumerable<BasePair>
    {
        private readonly byte[] _data;
        private readonly byte[] _qualities;

        internal Sequence(string id, byte[] data, byte[] qualities)
        {
            if (data.Length != qualities.Length)
            {
                throw new ArgumentException("Invalid input data");
            }
            Id = id;
            _data = data;
            _qualities = qualities;
        }

        public string Id { get; }

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
                yield return new BasePair((char)_data[i], _qualities[i]);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
