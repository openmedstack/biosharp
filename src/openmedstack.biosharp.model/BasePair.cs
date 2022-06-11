namespace OpenMedStack.BioSharp.Model
{
    public struct BasePair
    {
        private readonly char _letter;
        private readonly char _errorProbability;

        public BasePair(char letter, char errorProbability = (char)0)
        {
            _letter = letter;
            _errorProbability = errorProbability;
        }

        public char Letter
        {
            get { return _letter; }
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