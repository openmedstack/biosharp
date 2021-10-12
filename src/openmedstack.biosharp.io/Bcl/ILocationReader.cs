namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Collections.Generic;
    using Model.Bcl;

    public interface ILocationReader : IAsyncEnumerable<IPositionalData>, IAsyncDisposable
    {
    }
}