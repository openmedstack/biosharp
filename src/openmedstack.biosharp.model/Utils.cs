namespace OpenMedStack.BioSharp.Model;

using System;

public static class Utils
{
    extension(char qualityChar)
    {
        public double ToQuality()
        {
            var q = qualityChar - 33;
            var p = q / -10d;
            return Math.Pow(10, p);
        }
    }

    extension(double phred)
    {
        public double ToQuality()
        {
            return Math.Pow(10, -phred / 10d);
        }

        public double ToPhred()
        {
            return -10 * Math.Log10(phred);
        }

        public char ToQualChar()
        {
            return (char)(-10 * Math.Log10(phred) + 33);
        }
    }
}
