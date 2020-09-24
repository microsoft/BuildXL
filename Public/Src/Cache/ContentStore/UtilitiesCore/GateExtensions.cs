// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Contract = System.Diagnostics.ContractsLight.Contract;

namespace BuildXL.Cache.ContentStore.UtilitiesCore
{
    /// <nodoc />
    public static class GateExtensions
    {
        /// <summary>
        /// Convenience extension to allow tracing the time it takes to Wait a semaphore 
        /// </summary>
        public static async Task<TResult> GatedOperationAsync<TResult>(
            this SemaphoreSlim gate,
            Func<TimeSpan, int, Task<TResult>> operation,
            CancellationToken token = default,
            TimeSpan? ioGateTimeout = null)
        {
            Contract.RequiresNotNull(gate);
            Contract.RequiresNotNull(operation);

            var sw = Stopwatch.StartNew();
            var acquired = await gate.WaitAsync(ioGateTimeout ?? Timeout.InfiniteTimeSpan, token);

            if (!acquired)
            {
                throw new TimeoutException($"IO gate timed out after {ioGateTimeout}");
            }

            try
            {
                var currentCount = gate.CurrentCount;
                return await operation(sw.Elapsed, currentCount);
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Convenience extension to cheaply have only a single task running an operation, by using a semaphore as a latch.
        ///
        /// It will run <paramref name="operation"/> if the gate allows it to, or <paramref name="duplicated"/> otherwise.
        /// </summary>
        public static async Task<TResult> DeduplicatedOperationAsync<TResult>(this SemaphoreSlim gate, Func<TimeSpan, int, Task<TResult>> operation, Func<TimeSpan, int, Task<TResult>> duplicated, CancellationToken token = default)
        {
            Contract.RequiresNotNull(gate);
            Contract.RequiresNotNull(operation);
            Contract.RequiresNotNull(duplicated);

            var sw = Stopwatch.StartNew();
            var taken = await gate.WaitAsync(millisecondsTimeout: 0, token);
            if (!taken)
            {
                var currentCount = gate.CurrentCount;
                return await duplicated(sw.Elapsed, currentCount);
            }

            try
            {
                var currentCount = gate.CurrentCount;
                return await operation(sw.Elapsed, currentCount);
            }
            finally
            {
                gate.Release();
            }
        }

    }
}
