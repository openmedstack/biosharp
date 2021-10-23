namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System.Threading.Tasks;
    using Model.Bcl;

    public class DefaultQualityTrimmer : IQualityTrimmer
    {
        private static readonly IQualityTrimmer _trimmer = new DefaultQualityTrimmer();

        private DefaultQualityTrimmer()
        {

        }

        public static IQualityTrimmer Default => _trimmer;

        public Task<ReadData[]> Trim(ReadData[] data)
        {
            return Task.FromResult(data);
        }
    }
}
