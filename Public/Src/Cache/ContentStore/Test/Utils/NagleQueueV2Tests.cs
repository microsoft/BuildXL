// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Utilities.Collections;
using Xunit.Abstractions;

namespace ContentStoreTest.Utils
{
    public class NagleQueueV2Tests : NagleQueueTests
    {
        public NagleQueueV2Tests(ITestOutputHelper output)
            : base(output)
        {
        }

        /// <inheritdoc />
        protected override INagleQueue<T> CreateNagleQueue<T>(Func<T[], Task> processBatch, int maxDegreeOfParallelism, TimeSpan interval, int batchSize, bool start = true)
        {
            return start
                ? NagleQueue<T>.Create(processBatch, maxDegreeOfParallelism, interval, batchSize)
                : NagleQueue<T>.CreateUnstarted(processBatch, maxDegreeOfParallelism, interval, batchSize);
        }
    }
}
