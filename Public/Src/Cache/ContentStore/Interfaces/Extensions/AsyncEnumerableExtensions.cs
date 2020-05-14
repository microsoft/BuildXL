// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;

#nullable disable

namespace BuildXL.Cache.ContentStore.Interfaces.Extensions
{
    /// <summary>
    /// Extension methods for Async Enumerables.
    /// </summary>
    public static class AsyncEnumerableExtensions
    {
        /// <summary>
        /// Extension method for creating an async enumerable
        /// when there's a single call that produces the enumerable asynchronously followed by the items being enumerated.
        /// The type is a resultbase to guarantee that the producer task will not throw.
        /// </summary>
        public static System.Collections.Generic.IAsyncEnumerable<T> CreateSingleProducerTaskAsyncEnumerable<T>(
            Func<Task<IEnumerable<T>>> producerTaskFunc)
            where T : ResultBase
        {
            Task<IEnumerator<T>> producerTask = Task.Run(async () =>
            {
                var enumerable = await producerTaskFunc();
                return enumerable.GetEnumerator();
            });
            IEnumerator<T> enumerator = Enumerable.Empty<T>().GetEnumerator();
            return AsyncEnumerable.Create(
                (token) => AsyncEnumerator.Create(
                    async () =>
                    {
                        enumerator = await producerTask;
                        return enumerator.MoveNext();
                    },
                    () => enumerator.Current,
                    () =>
                    {
                        enumerator.Dispose();
                        return new ValueTask();
                    }));
        }

        /// <summary>
        /// Converts an enumerable of tasks to an async enumerable of task results
        /// </summary>
        public static System.Collections.Generic.IAsyncEnumerable<T> ToResultsAsyncEnumerable<T>(this IEnumerable<Task<T>> tasks)
        {
            return AsyncEnumerable.Create(
                (token) =>
                {
                    var enumerator = tasks.GetEnumerator();
                    T current = default;

                    return AsyncEnumerator.Create(
                        async () =>
                        {
                            if (enumerator.MoveNext())
                            {
                                current = await enumerator.Current;
                                return true;
                            }

                            return false;
                        },
                        () => current,
                        () =>
                        {
                            enumerator.Dispose();
                            return new ValueTask();
                        });
                });
        }

        /// <summary>
        /// Projects each element of an async-enumerable sequence into consecutive non-overlapping buffers which are produced based on element count information.
        /// </summary>
        public static IAsyncEnumerable<IList<TSource>> Buffer<TSource>(this IAsyncEnumerable<TSource> source, int count)
        {
            Contract.Requires(source != null);
            Contract.Requires(count > 0);
            
            return AsyncEnumerable.Create(core);

            async IAsyncEnumerator<IList<TSource>> core(CancellationToken cancellationToken)
            {
                var buffer = new List<TSource>(count);

                await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    buffer.Add(item);

                    if (buffer.Count == count)
                    {
                        yield return buffer;

                        buffer = new List<TSource>(count);
                    }
                }

                if (buffer.Count > 0)
                {
                    yield return buffer;
                }
            }
        }
    }
}
