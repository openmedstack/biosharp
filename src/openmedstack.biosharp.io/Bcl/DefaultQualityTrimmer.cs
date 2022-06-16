namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Threading.Tasks;
    using Model.Bcl;

    public class DefaultQualityTrimmer : IQualityTrimmer
    {
        private readonly char _minQuality;

        private DefaultQualityTrimmer(char minQuality)
        {
            _minQuality = minQuality;
        }

        public static IQualityTrimmer Instance { get; } = new DefaultQualityTrimmer((char)33);

        public Task<Memory<ReadData>> Trim(Memory<ReadData> data)
        {
            var span = data.Span;
            for (var i = 0; i < span.Length; i++)
            {
                var readData = span[i];
                if (readData.Qualities.Span[i] < _minQuality)
                {
                    readData.Qualities.Span[i] = (char)33;
                    readData.Bases.Span[i] = 'N';
                }
            }
            return Task.FromResult(data);
        }
    }
}
