namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Threading.Tasks;
    using Model.Bcl;

    public interface IQualityTrimmer
    {
        Task<Memory<ReadData>> Trim(Memory<ReadData> data);
    }
}
