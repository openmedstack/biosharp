namespace OpenMedStack.BioSharp.Model
{
    public struct BasePair
    {
        private readonly byte _letter;
        private readonly byte _errorProbability;

        public BasePair(byte letter, byte errorProbability = 0)
        {
            _letter = letter;
            _errorProbability = errorProbability;
        }

        public char Letter
        {
            get { return (char)_letter; }
        }

        public char ErrorProbability
        {
            get { return (char)_errorProbability; }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{Letter}/{ErrorProbability}";
        }
    }
}