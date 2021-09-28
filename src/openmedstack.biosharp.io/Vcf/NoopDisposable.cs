namespace OpenMedStack.BioSharp.Io.Vcf
{
    using System;

    internal struct NoopDisposable : IDisposable
    {
        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}