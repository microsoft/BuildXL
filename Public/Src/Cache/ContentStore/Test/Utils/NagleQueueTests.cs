// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tasks;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Utils
{
    public class NagleQueueTests : TestWithOutput
    {
        public NagleQueueTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void ResumeShouldTriggerBatchOnTime()
        {
            bool processBatchIsCalled = false;
            var queue = NagleQueue<int>.Create(
                processBatch: data =>
                              {
                                  processBatchIsCalled = true;
                                  return Task.FromResult(42);
                              },
                maxDegreeOfParallelism: 1,
                interval: TimeSpan.FromMilliseconds(1),
                batchSize: 10);

            var suspender = queue.Suspend();
            Thread.Sleep(1000);
            queue.Enqueue(42);
            suspender.Dispose(); // This should resume the queue and restart the timer

            Thread.Sleep(1000); // Definitely longer than the configured interval provided to NagleQueue

            // It means that the queue should call the callback and we can rely on that.

            Assert.True(processBatchIsCalled);
        }

        [Fact]
        public async Task ResumeShouldTriggerContractException()
        {
            // Have to run the test multiple times in order to check that there is no race conditions.
            int attemptCount = 10;
            for (int i = 0; i < attemptCount; i++)
            {
                await testCore(i);
            }

            static async Task testCore(int attempt)
            {
                var log = TestGlobal.Logger;
                TestGlobal.Logger.Debug($"Running {attempt} attempt.");
                Task task = null;

                using (var queue = NagleQueue<int>.Create(
                    processBatch: data => { return Task.FromResult(42); },
                    maxDegreeOfParallelism: 1,
                    interval: TimeSpan.FromMilliseconds(1),
                    batchSize: 100))
                {
                    var itemsEnqueuedSource = TaskSourceSlim.Create<object>();

                    task = Task.Run(
                        () =>
                        {
                            using (queue.Suspend())
                            {
                                for (int i = 0; i < 1_000_000; i++)
                                {
                                    queue.Enqueue(i);
                                }

                                itemsEnqueuedSource.SetResult(null);
                            }
                        });

                    // The items are added to the queue and the suspender is about to push all the items to the queue
                    await itemsEnqueuedSource.Task;
                    // Meanwhile, the queue itself will be disposed at the end of the block.
                    // So we're introducing a race condition between the suspender and the dispose method of the queue
                }

                await task; // the task should not fail!
            }
        }

        [Fact]
        public void EnqueingItemsInfrequentlyShouldAlwaysTriggerCallbackOnTime()
        {
            int processBatchIsCalled = 0;
            var queue = NagleQueue<int>.Create(
                processBatch: data =>
                              {
                                  processBatchIsCalled++;
                                  return Task.FromResult(42);
                              },
                maxDegreeOfParallelism: 1,
                interval: TimeSpan.FromMilliseconds(10),
                batchSize: 10);

            queue.Enqueue(42);
            
            Thread.Sleep(100);
            Assert.Equal(1, processBatchIsCalled);

            queue.Enqueue(42);

            Thread.Sleep(100); 
            Assert.Equal(2, processBatchIsCalled);
        }

        [Fact]
        public void PostAfterDispose()
        {
            var queue = NagleQueue<int>.Create(
                processBatch: data =>
                              {
                                  return Task.FromResult(42);
                              },
                maxDegreeOfParallelism: 1,
                interval: TimeSpan.FromMinutes(1),
                batchSize: 10);
            queue.Dispose();
            Assert.Throws<ObjectDisposedException>(() => queue.Enqueue(42));
        }

        [Fact]
        public void PendingItemsAreProcessedOnDispose()
        {
            bool processBatchWasCalled = false;
            var queue = NagleQueue<int>.Create(
                processBatch: data =>
                              {
                                  processBatchWasCalled = true;
                                  return Task.FromResult(42);
                              },
                maxDegreeOfParallelism: 1,
                interval: TimeSpan.FromMinutes(1),
                batchSize: 10);

            queue.Enqueue(42);
            Assert.False(processBatchWasCalled, "processBatch should not be called yet.");

            queue.Dispose();
            Assert.True(processBatchWasCalled, "processBatch should be called during disposal");
        }

        [Fact]
        public void ItemsInEagerBlocksAreProcessedEagerly()
        {
            int dataLength = 0;
            var processBatchWasCalled = false;
            var processBatchEvent = new ManualResetEvent(false);
            using (var queue = NagleQueue<int>.Create(
                processBatch: data =>
                              {
                                  dataLength = data.Length;
                                  processBatchWasCalled = true;
                                  processBatchEvent.Set();
                                  return Task.FromResult(42);
                              },
                maxDegreeOfParallelism: 1,
                interval: TimeSpan.FromMinutes(1),
                // batchSize is one, so the processing is eager.
                batchSize: 1))
            {
                queue.Enqueue(42);
                processBatchEvent.WaitOne(5000);
                Assert.True(processBatchWasCalled, "processBatch should be called eagerly.");
                Assert.Equal(1, dataLength);
            }
        }

        [Fact]
        public void TwoItemsAreProcessedInParallel()
        {
            var threads = new List<int>();
            using (var queue = NagleQueue<int>.Create(
                processBatch: async data =>
                              {
                                  lock (threads)
                                  {
                                      threads.Add(Thread.CurrentThread.ManagedThreadId);
                                  }

                                  await Task.Delay(1);
                              },
                maxDegreeOfParallelism: 2,
                interval: TimeSpan.FromSeconds(1),
                batchSize: 2))
            {
                queue.Enqueue(1);
                queue.Enqueue(2);
                queue.Enqueue(3);
                queue.Enqueue(4);
            }

            Assert.Equal(2, threads.Count);
        }

        [Fact]
        public void ItemsAreProcessedBasedOnInterval()
        {
            var processBatchWasCalled = false;
            var processBatchEvent = new ManualResetEvent(false);
            using (var queue = NagleQueue<int>.Create(
                processBatch: data =>
                              {
                                  processBatchWasCalled = true;
                                  processBatchEvent.Set();
                                  return Task.FromResult(42);
                              },
                maxDegreeOfParallelism: 1,
                interval: TimeSpan.FromMilliseconds(10),
                batchSize: 2))
            {
                queue.Enqueue(1);
                Assert.False(processBatchWasCalled);
                processBatchEvent.WaitOne(5000);
                Assert.True(processBatchWasCalled);
            }
        }

        [Fact]
        public void ItemsAreProcessedInBatches()
        {
            int batchSize = 0;
            using (var queue = NagleQueue<int>.Create(
                processBatch: data =>
                              {
                                  batchSize = data.Length;
                                  return Task.FromResult(42);
                              },
                maxDegreeOfParallelism: 1,
                interval: TimeSpan.FromMilliseconds(10),
                batchSize: 2))
            {
                queue.Enqueue(1);
                queue.Enqueue(2);
            }

            Assert.Equal(2, batchSize);
        }

        [Fact]
        public void TestExceptionHandling()
        {
            int callbackCount = 0;
            var queue = NagleQueue<int>.CreateUnstarted(
                maxDegreeOfParallelism: 1,
                interval: TimeSpan.FromMilliseconds(10),
                batchSize: 2);
            queue.Start(
                processBatch: async data =>
                              {
                                  callbackCount++;
                                  await Task.Yield();
                                  var e = new InvalidOperationException(string.Join(", ", data.Select(n => n.ToString())));
                                  throw e;
                              });
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
