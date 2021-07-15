namespace OpenMedStack.BioSharp.Calculations.Correlation
{
    using System;
    using System.Threading.Tasks;

    internal static class PearsonCorrelationCalculator
    {
        public static Task<double[]> AutoCorrelate(double[] source)
        {
            var length = source.Length / 2;
            var taskArray = new Task<double>[length];
            var x = new double[length];
            var y = new double[length];
            for (var index = 0; index < length; ++index)
            {
                x[index] = source[index];
                y[index] = source[index + 1];
                taskArray[index] = x.CalculatePearsonCorrelation(y);
            }
            return Task.WhenAll(taskArray);
        }

        public static Task<double> GetCorrelation(double[,] p, double[,] q)
        {
            if (p.Length != q.Length)
            {
                throw new InvalidOperationException("Cannot compare sets of different size.");
            }
            return Task.Run(() =>
            {
                var width = p.GetLength(0);
                var height = p.GetLength(1);

                if (width != q.GetLength(0) || height != q.GetLength(1))
                {
                    throw new ArgumentException("Input vectors must be of the same dimension.");
                }

                double pSum = 0, qSum = 0, pSumSq = 0, qSumSq = 0, productSum = 0;

                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var pValue = p[x, y];
                        var qValue = q[x, y];

                        pSum += pValue;
                        qSum += qValue;
                        pSumSq += pValue * pValue;
                        qSumSq += qValue * qValue;
                        productSum += pValue * qValue;
                    }
                }

                var numerator = productSum - pSum * qSum / height;
                var denominator = Math.Sqrt((pSumSq - pSum * pSum / height) * (qSumSq - qSum * qSum / height));

                return denominator.Equals(0.0) ? 0 : numerator / denominator;
            });
        }

        public static Task<double> GetCorrelation(byte[,] p, byte[,] q)
        {
            if (p.Length != q.Length)
            {
                throw new InvalidOperationException("Cannot compare sets of different size.");
            }
            return Task.Run(() =>
            {
                var width = p.GetLength(0);
                var height = p.GetLength(1);

                if (width != q.GetLength(0) || height != q.GetLength(1))
                {
                    throw new ArgumentException("Input vectors must be of the same dimension.");
                }

                int pSum = 0, qSum = 0, pSumSq = 0, qSumSq = 0, productSum = 0;

                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var pValue = p[x, y];
                        var qValue = q[x, y];

                        pSum += pValue;
                        qSum += qValue;
                        pSumSq += pValue * pValue;
                        qSumSq += qValue * qValue;
                        productSum += pValue * qValue;
                    }
                }

                var numerator = productSum - pSum * qSum / height;
                var denominator = Math.Sqrt((pSumSq - pSum * pSum / height) * (qSumSq - qSum * qSum / height));

                return denominator.Equals(0.0) ? 0 : numerator / denominator;
            });
        }
    }
}
