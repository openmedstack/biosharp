namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System.Threading.Tasks;
    using Model.Bcl;

    public interface IQualityTrimmer
    {
        Task<ReadData[]> Trim(ReadData[] data);
    }
}
