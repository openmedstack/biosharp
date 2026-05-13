using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenMedStack.BioSharp.Calculations.Alignment;

/// <summary>
/// Extension for converting collections to IAsyncEnumerable.
/// </summary>
public static class AsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            await Task.Yield();
            yield return item;
        }
    }
}