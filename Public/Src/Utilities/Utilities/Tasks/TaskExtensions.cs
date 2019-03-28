// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;

namespace BuildXL.Utilities.Tasks
{
    /// <summary>
    /// Set of helper and extension methods for TPL-based operations.
    /// </summary>
    public static class TaskExtensions
    {
        /// <summary>
        /// Iterates over <paramref name="source"/> sequence and launches tasks by calling <paramref name="selector"/> function.
        /// </summary>
        /// <remarks>
        /// The method makes sure that there is not more than <paramref name="degreeOfParallelism"/> pending tasks executed at the same time.
        /// </remarks>
        public static async Task<Possible<TResult>[]> ForEachAsync<T, TResult>(
            this IReadOnlyList<T> source,
            int degreeOfParallelism,
            Func<T, Task<Possible<TResult>>> selector,
            CancellationToken token)
        {
            Contract.Requires(source != null);
            Contract.Requires(degreeOfParallelism > 0);
            Contract.Requires(selector != null);

            if (source.Count == 0)
            {
                return CollectionUtilities.EmptyArray<Possible<TResult>>();
            }
            
            var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var semaphore = new SemaphoreSlim(degreeOfParallelism, degreeOfParallelism);

            var result = await Task.WhenAll(source.Select(e => ProcessAsync(e)));

            return result.Where(r => r != null).Select(r => r.Value).ToArray();

            async Task<Possible<TResult>?> ProcessAsync(T value)
            {
                if (cts.Token.IsCancellationRequested)
                {
                    return null;
                }

                bool semaphoreAcquired = false;
                try
                {
                    await semaphore.WaitAsync(cts.Token);
                    semaphoreAcquired = true;
                    var selectorResult = await selector(value);
                    if (!selectorResult.Succeeded)
                    {
                        cts.Cancel();
                    }
                    return selectorResult;
                }
                catch (TaskCanceledException)
                {
                    return null;
                }
                catch (Exception e)
                {
                    cts.Cancel();

                    // Use ExceptionDispatchInfo for preserving the original stack trace.
                    // 'throw;' unfortunately messes up with it on the Desktop CLR.
                    ExceptionDispatchInfo.Capture(e).Throw();
                    throw;
                }
                finally
                {
                    // No need to release a semaphore if the semaphore was not incremented successfully.
                    if (semaphoreAcquired)
                    {
                        semaphore.Release();
                    }
                }
            }
        }
    }
}
