namespace OpenMedStack.BioSharp.Io
{
    using System.Collections;

    internal static class Extensions
    {
        public static char ToChar(this bool a, bool b, bool c)
        {
            return a switch
            {
                false when !b && c => 'A',
                false when b && !c => 'C',
                false when b && c => 'G',
                true when !b && !c => 'T',
                true when b && !c => 'N',
                _ => '*'
            };
        }

        public static BitArray ToBits(this char letter)
        {
            return letter switch
            {
                'A' => new BitArray(new[] { false, false, true }),
                'C' => new BitArray(new[] { false, true, false }),
                'G' => new BitArray(new[] { false, true, true }),
                'T' => new BitArray(new[] { true, false, false }),
                'N' => new BitArray(new[] { true, true, false }),
                _ => new BitArray(new[] { false, false, false })
            };
        }
    }
}