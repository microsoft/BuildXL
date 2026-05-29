// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
#if NET10_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;

#nullable disable

namespace BuildXL.Cache.ContentStore.Interfaces.Extensions
{
    /// <summary>
    /// Extension methods for Async Enumerables.
    /// </summary>
    /// <remarks>
    /// .NET 10 promoted <see cref="System.Linq.AsyncEnumerable"/> into the BCL, which collides with
    /// the System.Linq.Async NuGet package's same-named type (CS0433). The package is therefore
    /// skipped for net10 builds (see Public\Sdk\SelfHost\BuildXL\BuildXLSdk.Packages.dsc), so the
    /// AsyncEnumerable.Create / AsyncEnumerator.Create factories from System.Linq.Async are not
    /// available under NET10_0_OR_GREATER. The C#-native async IAsyncEnumerable +
    /// `yield return` form used in the NET10_0_OR_GREATER branches is functionally equivalent.
    /// </remarks>
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

#if NET10_0_OR_GREATER
            return Impl(producerTask);

            static async System.Collections.Generic.IAsyncEnumerable<T> Impl(Task<IEnumerator<T>> producerTask)
            {
                var enumerator = await producerTask;
                try
                {
                    while (enumerator.MoveNext())
                    {
                        yield return enumerator.Current;
                    }
                }
                finally
                {
                    enumerator.Dispose();
                }
            }
#else
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
#endif
        }

        /// <summary>
        /// Converts an enumerable of tasks to an async enumerable of task results
        /// </summary>
        public static System.Collections.Generic.IAsyncEnumerable<T> ToResultsAsyncEnumerable<T>(this IEnumerable<Task<T>> tasks)
        {
#if NET10_0_OR_GREATER
            return Impl();

            async System.Collections.Generic.IAsyncEnumerable<T> Impl()
            {
                using var enumerator = tasks.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    yield return await enumerator.Current;
                }
            }
#else
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
#endif
        }

        /// <summary>
        /// Projects each element of an async-enumerable sequence into consecutive non-overlapping buffers which are produced based on element count information.
        /// </summary>
        public static IAsyncEnumerable<IList<TSource>> Buffer<TSource>(this IAsyncEnumerable<TSource> source, int count)
        {
            Contract.Requires(source != null);
            Contract.Requires(count > 0);

#if NET10_0_OR_GREATER
            // The `await foreach` body below is identical to the `core` body in the #else branch,
            // but the two can't be merged into a shared helper: the enclosing functions have
            // different return types (`IAsyncEnumerable` here vs `IAsyncEnumerator` for the net9
            // factory passed to AsyncEnumerable.Create), and `yield return` can't be relayed
            // through a non-iterator helper.
            return Core(source, count);

            // [EnumeratorCancellation] makes the C# compiler thread the consumer's cancellation
            // token (from `WithCancellation(...)`) into `cancellationToken` at iteration time,
            // mirroring how the original `AsyncEnumerable.Create(core)` plumbed the token into core.
            static async IAsyncEnumerable<IList<TSource>> Core(
                IAsyncEnumerable<TSource> source,
                int count,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
#else
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
#endif
        }
    }
}
