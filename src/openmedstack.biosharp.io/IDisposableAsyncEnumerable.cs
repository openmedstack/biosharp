using System;
using System.Collections.Generic;

namespace OpenMedStack.BioSharp.Io;

public interface IDisposableAsyncEnumerable<out T> : IAsyncDisposable, IAsyncEnumerable<T>
{
}
