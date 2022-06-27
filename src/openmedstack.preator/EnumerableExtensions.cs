namespace OpenMedStack.Preator;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

internal static class EnumerableExtensions
{
    public static IAsyncEnumerable<TOut> SelectManyParallel<TInput, TOut>(
        this IAsyncEnumerable<Task<IEnumerable<TInput>>> coldTasks,
        int degreeOfParallelism,
        Func<IEnumerable<TInput>, IAsyncEnumerable<TOut>> selector,
        CancellationToken cancellationToken = default)
    {
        return coldTasks.ExecuteParallel(degreeOfParallelism, cancellationToken).SelectMany(selector);
    }

    public static IAsyncEnumerable<TOut> SelectParallel<TInput, TOut>(
        this IAsyncEnumerable<Task<TInput>> coldTasks,
        int degreeOfParallelism,
        Func<TInput, TOut> selector,
        CancellationToken cancellationToken = default)
    {
        return coldTasks.ExecuteParallel(degreeOfParallelism, cancellationToken).Select(selector);
    }

    public static async IAsyncEnumerable<TResult> ExecuteParallel<TResult>(
        this IAsyncEnumerable<Task<TResult>> coldTasks,
        int degreeOfParallelism,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (degreeOfParallelism < 1)
            throw new ArgumentOutOfRangeException(nameof(degreeOfParallelism));

        if (coldTasks is ICollection<Task<TResult>>)
            throw new ArgumentException("The enumerable should not be materialized.", nameof(coldTasks));

        var semaphore = new SemaphoreSlim(1);
        var queue = new ConcurrentQueue<Task<TResult>>();

        var enumerator = coldTasks.GetAsyncEnumerator(cancellationToken);
        await using var _ = enumerator.ConfigureAwait(false);

        for (var index = 0; index < degreeOfParallelism && await EnqueueNextTask().ConfigureAwait(false); index++)
        {
            ;
        }

        while (queue.TryDequeue(out var nextTask))
        {
            yield return await nextTask.ConfigureAwait(false);
        }

        async Task<bool> EnqueueNextTask()
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!await enumerator.MoveNextAsync().ConfigureAwait(false)) return false;

            var nextTask = enumerator.Current.ContinueWith(
                async t =>
                {
                    await EnqueueNextTask();
                    return await t.ConfigureAwait(false);
                },
                cancellationToken);
            queue.Enqueue(nextTask.Unwrap());
            semaphore.Release();
            return true;
        }
    }

    /// <summary>
    /// Split the elements of a sequence into chunks of size at most <paramref name="size"/>.
    /// </summary>
    /// <remarks>
    /// Every chunk except the last will be of size <paramref name="size"/>.
    /// The last chunk will contain the remaining elements and may be of a smaller size.
    /// </remarks>
    /// <param name="source">
    /// An <see cref="IEnumerable{T}"/> whose elements to chunk.
    /// </param>
    /// <param name="size">
    /// Maximum size of each chunk.
    /// </param>
    /// <typeparam name="TSource">
    /// The type of the elements of source.
    /// </typeparam>
    /// <returns>
    /// An <see cref="IEnumerable{T}"/> that contains the elements the input sequence split into chunks of size <paramref name="size"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="size"/> is below 1.
    /// </exception>
    public static IAsyncEnumerable<TSource[]> Chunk<TSource>(this IAsyncEnumerable<TSource> source, int size)
    {
        return ChunkIterator(source, size);
    }

    private static async IAsyncEnumerable<TSource[]> ChunkIterator<TSource>(IAsyncEnumerable<TSource> source, int size)
    {
        var e = source.GetAsyncEnumerator();
        await using var _ = e.ConfigureAwait(false);
        while (await e.MoveNextAsync().ConfigureAwait(false))
        {
            var chunk = new TSource[size];
            chunk[0] = e.Current;

            var i = 1;
            for (; i < chunk.Length && await e.MoveNextAsync().ConfigureAwait(false); i++)
            {
                chunk[i] = e.Current;
            }

            if (i == chunk.Length)
            {
                yield return chunk;
            }
            else
            {
                Array.Resize(ref chunk, i);
                yield return chunk;
                yield break;
            }
        }
    }
}