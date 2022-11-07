// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using System.Linq;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.ParallelAlgorithms;
using FluentAssertions;
using Xunit;
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
                ? NagleQueueV2<T>.Create(items => processBatch(items.ToArray()), maxDegreeOfParallelism, interval, batchSize)
                : NagleQueueV2<T>.CreateUnstarted(items => processBatch(items.ToArray()), maxDegreeOfParallelism, interval, batchSize);
        }

        [Fact]
        public void TestExceptionHandling()
        {
            // This test hangs for the old implementation because due to subtle race condition
            // the action block used by the nagle queue might ended up in failed state when
            // the batch block still has unprocessed items.
            // We're going to retire the old impelmentation so its not worth fixing the race condition in the code that was always been there
            // and that is going to be deleted in the neer future.
            int callbackCount = 0;
            var queue = CreateNagleQueue<int>(
                async data =>
                {
                    callbackCount++;
                    await Task.Yield();
                    var e = new InvalidOperationException(string.Join(", ", data.Select(n => n.ToString())));
                    throw e;
                },
                maxDegreeOfParallelism: 1,
                interval: TimeSpan.FromMilliseconds(10),
                batchSize: 2,
                start: false);
            queue.Start();
            queue.Enqueue(1);
            queue.Enqueue(2);
            queue.Enqueue(3);

            // And if callback fails, the queue itself moves to a faulted state.
            // This will manifest itself in an error during Dispose invocation.
            // This is actually quite problematic, because Dispose method can be called
            // from the finally block (explicitly, or implicitly via using block)
            // and in this case the original exception that caused the finally block invocation
            // will be masked by the exception from Dispose method.
            // Work item: 1741215

            // Dispose method propagates the error thrown in the callback.
            Assert.Throws<InvalidOperationException>(() => queue.Dispose());

            // Once callback fails, it won't be called any more
            callbackCount.Should().Be(1);
        }
    }
}
