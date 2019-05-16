// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;

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
        public static Async::System.Collections.Generic.IAsyncEnumerable<T> CreateSingleProducerTaskAsyncEnumerable<T>(
            Func<Task<IEnumerable<T>>> producerTaskFunc)
            where T : ResultBase
        {
            Task<IEnumerator<T>> producerTask = Task.Run(async () =>
            {
                var enumerable = await producerTaskFunc();
                return enumerable.GetEnumerator();
            });
            IEnumerator<T> enumerator = Enumerable.Empty<T>().GetEnumerator();
            return AsyncEnumerable.CreateEnumerable(
                () => AsyncEnumerable.CreateEnumerator(
                    async cancellationToken =>
                    {
                        enumerator = await producerTask;
                        return enumerator.MoveNext();
                    },
                    () => enumerator.Current,
                    () => { enumerator.Dispose(); }));
        }

        /// <summary>
        /// Converts an enumerable of tasks to an async enumerable of task results
        /// </summary>
        public static Async::System.Collections.Generic.IAsyncEnumerable<T> ToResultsAsyncEnumerable<T>(this IEnumerable<Task<T>> tasks)
        {
            return AsyncEnumerable.CreateEnumerable(
                () =>
                {
                    var enumerator = tasks.GetEnumerator();
                    T current = default;

                    return AsyncEnumerable.CreateEnumerator(
                        async cancellationToken =>
                        {
                            if (enumerator.MoveNext())
                            {
                                current = await enumerator.Current;
                                return true;
                            }

                            return false;
                        },
                        () => current,
                        () => { enumerator.Dispose(); });
                });
        }
    }
}
