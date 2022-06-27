namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using Model.Bcl;

    public interface IQualityTrimmer
    {
        bool Trim(Memory<ReadData> data);
    }
}
