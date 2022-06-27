namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using Model;
    using Model.Bcl;

    public class DefaultQualityTrimmer : IQualityTrimmer
    {
        private readonly double _minQuality;

        private DefaultQualityTrimmer(double minQuality)
        {
            _minQuality = minQuality;
        }

        public static IQualityTrimmer Instance { get; } = new DefaultQualityTrimmer(20);

        public bool Trim(Memory<ReadData> data)
        {
            foreach (var readData in data.Span)
            {
                double sum = 0;
                for (var j = 0; j < readData.Qualities.Length; j++)
                {
                    sum += readData.Qualities.Span[j];
                }

                var average = sum / readData.Qualities.Length;
                if (average <= _minQuality)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
