namespace OpenMedStack.BioSharp.Calculations.Correlation
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    public static class PearsonCorrelationExtensions
    {
        public static Task<double> CalculatePearsonCorrelation(this IEnumerable<double> source, IEnumerable<double> other)
        {
            var s = source.ToArray();
            var o = other.ToArray();
            var sourceArray = new double[1, s.Length];
            var otherArray = new double[1, s.Length];
            for (var i = 0; i < s.Length; i++)
            {
                sourceArray[0, i] = s[i];
            }

            for (var i = 0; i < o.Length; i++)
            {
                otherArray[0, i] = o[i];
            }

            return CalculatePearsonCorrelation(sourceArray, otherArray);
        }

        public static Task<double> CalculatePearsonCorrelation(this IEnumerable<byte> source, IEnumerable<byte> other)
        {
            var s = source.ToArray();
            var o = other.ToArray();
            var sourceArray = new byte[1, s.Length];
            var otherArray = new byte[1, s.Length];
            for (var i = 0; i < s.Length; i++)
            {
                sourceArray[0, i] = s[i];
            }

            for (var i = 0; i < o.Length; i++)
            {
                otherArray[0, i] = o[i];
            }

            return CalculatePearsonCorrelation(sourceArray, otherArray);
        }

        public static Task<double> CalculatePearsonCorrelation(this double[,] source, double[,] other)
        {
            return PearsonCorrelationCalculator.GetCorrelation(source, other);
        }

        public static Task<double> CalculatePearsonCorrelation(this byte[,] source, byte[,] other)
        {
            return PearsonCorrelationCalculator.GetCorrelation(source, other);
        }

        public static Task<double[]> PearsonAutoCorrelate(this IEnumerable<double> source)
        {
            return PearsonCorrelationCalculator.AutoCorrelate(source.ToArray());
        }
    }
}
