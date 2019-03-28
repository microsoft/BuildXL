// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Special version of <see cref="NagleQueue{T}"/> that runs batch processing synchronously.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal sealed class SynchronousNagleQueue<T> : NagleQueue<T>
    {
        private  Func<T[], Task> _processBatch;

        /// <nodoc />
        internal SynchronousNagleQueue(int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
            : base(maxDegreeOfParallelism, interval, batchSize)
        {
            Contract.Requires(maxDegreeOfParallelism == 1);
            Contract.Requires(batchSize == 1);
        }

        /// <inheritdoc />
        public override void Start(Func<T[], Task> processBatch) => _processBatch = processBatch;

        /// <inheritdoc />
        protected override void EnqueueCore(T item)
        {
            Contract.Assert(_processBatch != null, "Did you forget to call Start method?");

            _processBatch(new[] {item}).GetAwaiter().GetResult();
        }
    }
}
