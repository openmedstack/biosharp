namespace OpenMedStack.BioSharp.Model
{
    public struct BasePair
    {
        private readonly char _letter;
        private readonly byte _quality;

        public BasePair(char letter, byte quality)
        {
            _letter = letter;
            _quality = quality;
        }

        public char Letter
        {
            get { return _letter; }
        }

        public byte Quality
        {
            get { return _quality; }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{_letter}/{_quality}";
        }
    }
}