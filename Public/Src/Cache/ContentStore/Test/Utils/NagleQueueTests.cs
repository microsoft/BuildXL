// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Utils;
using Xunit;

namespace ContentStoreTest.Utils
{
    public class NagleQueueTests
    {
        [Fact]
        public void ResumeShouldTriggerBatchOnTime()
        {
            bool processBatchIsCalled = false; ;
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
                interval: TimeSpan.FromMinutes(1),
                // batchSize is one, so the processing is eager.
                batchSize: 1))
            {
                queue.Enqueue(42);
                processBatchEvent.WaitOne(5000);
                Assert.True(processBatchWasCalled, "processBatch should be called eagerly.");
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
    }
}
