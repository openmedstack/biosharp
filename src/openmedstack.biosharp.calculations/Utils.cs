namespace OpenMedStack.BioSharp.Calculations
{
    using System;

    public static class Utils
    {
        public static char ToQualChar(this double probability)
        {
            return (char)(-10 * Math.Log10(probability) + 33);
        }

        public static double ToQuality(this char qualityChar)
        {
            var q = qualityChar - 33;
            var p = q / -10d;
            return Math.Pow(10, p);
        }

        public static double ToQuality(this double phred)
        {
            return Math.Pow(10, -phred / 10d);
        }

        public static double ToPhred(this double likelihood)
        {
            return -10 * Math.Log10(likelihood);
        }
    }
}