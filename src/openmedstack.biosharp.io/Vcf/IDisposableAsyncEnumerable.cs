namespace OpenMedStack.BioSharp.Io.Vcf
{
    using System;
    using System.Collections.Generic;

    public interface IDisposableAsyncEnumerable<out T> : IAsyncDisposable, IAsyncEnumerable<T> { }
}