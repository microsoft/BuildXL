// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Special version of <see cref="NagleQueue{T}"/> that runs batch processing synchronously.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal sealed class SynchronousNagleQueue<T> : NagleQueue<T>
    {
        private readonly Func<T[], Task> _processBatch;

        /// <nodoc />
        internal SynchronousNagleQueue(Func<T[], Task> processBatch, int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
            : base(processBatch, maxDegreeOfParallelism, interval, batchSize)
        {
            Contract.Requires(maxDegreeOfParallelism == 1);
            Contract.Requires(batchSize == 1);
            _processBatch = processBatch;
        }

        /// <inheritdoc />
        protected override void EnqueueCore(T item, bool fromResume)
        {
            Contract.Assert(_processBatch != null, "Did you forget to call Start method?");

            _processBatch(new[] {item}).GetAwaiter().GetResult();
        }
    }
}
