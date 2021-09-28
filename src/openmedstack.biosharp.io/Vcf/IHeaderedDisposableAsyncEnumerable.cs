namespace OpenMedStack.BioSharp.Io.Vcf
{
    using System;
    using System.Collections.Generic;

    public interface IHeaderedDisposableAsyncEnumerable<out THeader, out T> : IAsyncDisposable, IAsyncEnumerable<T>
    {
        public THeader Header { get; }
    }
}