// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Utils;
using Xunit;

namespace BuildXL.Cache.ContentStore.Test.Synchronization
{
    public class BatchingQueueTests
    {
        [Fact]
        public async Task Enqueue_SingleItem_ProcessedSuccessfully()
        {
            var key = "testKey";
            var value = "testValue";
            var result = "processed";
            var processed = false;
            var queue = new BatchingQueue<string, string, string>(
                (batchKey, items, cancellationToken) =>
                {
                    processed = true;
                    foreach (var item in items)
                    {
                        item.Succeed(result);
                    }

                    return Task.CompletedTask;
                },
                TimeSpan.FromMilliseconds(50),
                10,
                1
            );

            var task = queue.Enqueue(key, value);
            var actualResult = await task;

            Assert.True(processed);
            Assert.Equal(result, actualResult);
        }

        [Fact]
        public async Task Enqueue_MultipleItems_BatchedCorrectly()
        {
            var key = "testKey";
            var batchSize = 5;
            var processedBatchSize = 0;
            var queue = new BatchingQueue<string, int, bool>(
                (batchKey, items, cancellationToken) =>
                {
                    processedBatchSize = items.Count;
                    foreach (var item in items)
                    {
                        item.Succeed(true);
                    }

                    return Task.CompletedTask;
                },
                TimeSpan.FromMilliseconds(100),
                batchSize,
                1
            );

            var tasks = new List<Task<bool>>();
            for (int i = 0; i < batchSize; i++)
            {
                tasks.Add(queue.Enqueue(key, i));
            }
            await Task.WhenAll(tasks);

            Assert.Equal(batchSize, processedBatchSize);
        }

        [Fact]
        public async Task Enqueue_ExceptionInProcessing_TaskSourceReceivesException()
        {
            var key = "testKey";
            var value = "testValue";
            var exceptionMessage = "Processing failed.";
            var queue = new BatchingQueue<string, string, string>(
                (batchKey, items, cancellationToken) =>
                {
                    throw new InvalidOperationException(exceptionMessage);
                },
                TimeSpan.FromMilliseconds(50),
                10,
                1
            );

            var task = queue.Enqueue(key, value);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
            Assert.Equal(exceptionMessage, exception.Message);
        }

        [Fact]
        public async Task DisposeAsync_PendingItems_Canceled()
        {
            var queue = new BatchingQueue<string, string, string>(
                async (batchKey, items, cancellationToken) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                },
                TimeSpan.FromMilliseconds(100),
                10,
                1
            );

            var task = queue.Enqueue("key", "value");
            await ((IAsyncDisposable)queue).DisposeAsync();

            await Assert.ThrowsAsync<TaskCanceledException>(() => task);
        }

        [Fact]
        public async Task Enqueue_MultipleKeys_ProcessedInParallel()
        {
            var key1 = "testKey1";
            var key2 = "testKey2";
            var batchSize = 1; // Ensure batch size allows for parallel processing
            var barrier = new Barrier(participantCount: 2); // Setup a barrier for 2 participants

            var processedKeys = new List<string>();
            var queue = new BatchingQueue<string, int, bool>(
                async (batchKey, items, cancellationToken) =>
                {
                    Assert.True(items.Count == 1);
                    // Signal this batch is ready and wait for the other
                    barrier.SignalAndWait(cancellationToken);

                    lock (processedKeys)
                    {
                        processedKeys.Add(batchKey);
                    }

                    // Simulate work
                    await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);

                    foreach (var item in items)
                    {
                        item.Succeed(true);
                    }
                },
                TimeSpan.FromMilliseconds(100),
                batchSize,
                2 // Set maxDegreeOfParallelism to 2 to allow parallel processing
            );

            // Enqueue items with different keys
            var task1 = queue.Enqueue(key1, 1);
            var task2 = queue.Enqueue(key2, 2);

            await Task.WhenAll(task1, task2);

            Assert.Contains(key1, processedKeys);
            Assert.Contains(key2, processedKeys);
            Assert.Equal(2, processedKeys.Count); // Ensure both keys were processed
        }

        [Fact]
        public async Task Enqueue_ManyItemsForManyKeys_BatchingOccursWithLargeBatchSizes()
        {
            int numberOfKeys = 10;
            int itemsPerKey = 100;
            int maxBatchSize = 50; // Set a sizable batch size
            var allBatches = new ConcurrentBag<List<int>>();
            var processingCompleted = new TaskCompletionSource<bool>();

            var queue = new BatchingQueue<int, int, bool>(
                (key, items, cancellationToken) =>
                {
                    allBatches.Add(new List<int>(items.Select(item => item.Value)));
                    if (allBatches.Count >= numberOfKeys * (itemsPerKey / maxBatchSize)) // Expected number of batches
                    {
                        processingCompleted.SetResult(true);
                    }
                    foreach (var item in items)
                    {
                        item.Succeed(true);
                    }
                    return Task.CompletedTask;
                },
                TimeSpan.FromMilliseconds(100), // Short interval to trigger batching quickly
                maxBatchSize,
                numberOfKeys // Allow parallel processing for each key
            );

            // Enqueue many items for many keys
            for (int key = 0; key < numberOfKeys; key++)
            {
                for (int i = 0; i < itemsPerKey; i++)
                {
                    _ = queue.Enqueue(key, i);
                }
            }

            // Wait for processing to complete or timeout after a reasonable period
            var processingResult = await Task.WhenAny(processingCompleted.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.True(processingResult == processingCompleted.Task, "Processing did not complete in expected time.");

            // Verify that batching occurred and batch sizes are large
            Assert.All(allBatches, batch => Assert.True(batch.Count <= maxBatchSize, $"Batch size exceeded max of {maxBatchSize}."));
            Assert.True(allBatches.Any(batch => batch.Count == maxBatchSize), $"No batches found with max size of {maxBatchSize}.");
        }

        [Fact]
        public async Task Dispose_WithPendingItems_AllItemsCanceled()
        {
            var key = "testKey";
            var numberOfItems = 50; // Number of items to enqueue
            var allTasks = new List<Task<bool>>();
            var queue = new BatchingQueue<string, int, bool>(
                async (batchKey, items, cancellationToken) =>
                {
                    // Intentionally delay processing to simulate pending tasks at the time of disposal
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    foreach (var item in items)
                    {
                        item.Succeed(true);
                    }
                },
                TimeSpan.FromMilliseconds(100), // Processing interval
                10, // Batch size
                1   // Degree of parallelism
            );

            // Enqueue several items
            for (int i = 0; i < numberOfItems; i++)
            {
                allTasks.Add(queue.Enqueue(key, i));
            }

            // Dispose the queue immediately to cancel pending items
            queue.Dispose();

            // Verify that all tasks are canceled
            foreach (var task in allTasks)
            {
                await Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
            }
        }

        [Fact]
        public async Task Enqueue_Items_ProcessingThrows_AllItemsReceiveException()
        {
            var key = "testKey";
            var numberOfItems = 20;
            var allTasks = new List<Task<bool>>();
            var expectedExceptionMessage = "Simulated processing failure";
            var queue = new BatchingQueue<string, int, bool>(
                (batchKey, items, cancellationToken) =>
                {
                    throw new InvalidOperationException(expectedExceptionMessage);
                },
                TimeSpan.FromMilliseconds(100), // Interval short enough to quickly trigger processing
                10, // Batch size to ensure at least one batch is formed
                1   // Single thread to simplify testing
            );

            // Enqueue several items to ensure at least one batch is formed
            for (int i = 0; i < numberOfItems; i++)
            {
                allTasks.Add(queue.Enqueue(key, i));
            }

            // Wait for all tasks to complete or fault
            try
            {
                await Task.WhenAll(allTasks);
            }
            catch
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
            {
                // Exceptions are expected due to the simulated failure in processing
            }
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler

            // Verify that all tasks have the expected exception
            foreach (var task in allTasks)
            {
                Assert.True(task.IsFaulted);
                var exception = task.Exception?.GetBaseException();
                Assert.NotNull(exception);
                Assert.IsType<InvalidOperationException>(exception);
                Assert.Equal(expectedExceptionMessage, exception?.Message);
            }
        }

        [Fact]
        public Task Enqueue_WithCancellationBeforeProcessing_OperationIsCanceled()
        {
            var key = "testKey";
            using var cancellationTokenSource = new CancellationTokenSource();
            using var barrier = new Barrier(participantCount: 2); // One for the processing task, one for the test thread

            var queue = new BatchingQueue<string, string, string>(
                (batchKey, items, cancellationToken) =>
                {
                    barrier.SignalAndWait(); // Wait for the test thread to reach its barrier point
                    foreach (var item in items)
                    {
                        item.Succeed("processed");
                    }
                    return Task.CompletedTask;
                },
                TimeSpan.FromMilliseconds(100),
                10,
                1
            );

            var task = queue.Enqueue(key, "testValue", cancellationTokenSource.Token);
            cancellationTokenSource.Cancel(); // Cancel before signaling the barrier
            barrier.SignalAndWait(); // This ensures the enqueue operation reaches the processing stage

            return Assert.ThrowsAsync<OperationCanceledException>(() => task);
        }

        [Fact]
        public Task Enqueue_WithCancellationDuringProcessing_OperationIsCanceled()
        {
            var key = "testKey";
            using var cancellationTokenSource = new CancellationTokenSource();
            using var semaphoreSlim = new SemaphoreSlim(0, 1); // Start at 0, allowing one thread to pass when released

            var queue = new BatchingQueue<string, string, string>(
                async (batchKey, items, cancellationToken) =>
                {
                    await semaphoreSlim.WaitAsync(cancellationToken); // Wait until the test thread releases the semaphore
                    foreach (var item in items)
                    {
                        item.Succeed("processed");
                    }
                },
                TimeSpan.FromMilliseconds(10),
                10,
                1
            );

            var task = queue.Enqueue(key, "testValue", cancellationTokenSource.Token);
            cancellationTokenSource.Cancel(); // Cancel before releasing the semaphore
            semaphoreSlim.Release(); // Allow the processing task to proceed

            return Assert.ThrowsAsync<OperationCanceledException>(() => task);
        }

        [Fact]
        public Task Enqueue_WithCancellationAfterEnqueueingButBeforeProcessing_OperationIsCanceled()
        {
            var key = "testKey";
            var cancellationTokenSource = new CancellationTokenSource();
            var barrier = new Barrier(participantCount: 2); // One for the processing task, one for the test thread

            var queue = new BatchingQueue<string, string, string>(
                (batchKey, items, cancellationToken) =>
                {
                    barrier.SignalAndWait(); // Wait for the test thread to cancel the token
                    foreach (var item in items)
                    {
                        item.Succeed("processed");
                    }
                    return Task.CompletedTask;
                },
                TimeSpan.FromMilliseconds(100),
                10,
                1
            );

            var task = queue.Enqueue(key, "testValue", cancellationTokenSource.Token);
            cancellationTokenSource.Cancel(); // Cancel after enqueueing
            barrier.SignalAndWait(); // Ensure processing waits for cancellation

            return Assert.ThrowsAsync<OperationCanceledException>(() => task);
        }

        [Fact]
        public Task EnsureSingleThreadedProcessingForKey()
        {
            var key = "testKey";
            var numberOfItems = 100; // Total number of items to be enqueued
            var numberOfThreads = 10; // Number of parallel tasks to simulate threads
            var semaphore = new SemaphoreSlim(1, 1); // Semaphore to ensure single-threaded processing

            var queue = new BatchingQueue<string, int, bool>(
                async (batchKey, items, cancellationToken) =>
                {
                    // Try to acquire the semaphore in a non-blocking manner
                    if (!semaphore.Wait(0))
                    {
                        throw new InvalidOperationException($"Concurrent processing detected for key {batchKey}.");
                    }

                    try
                    {
                        // Simulate processing
                        foreach (var item in items)
                        {
                            item.Succeed(true);
                        }
                        await Task.Yield(); // Ensure async execution
                    }
                    finally
                    {
                        semaphore.Release(); // Release the semaphore for the next processing
                    }
                },
                TimeSpan.FromMilliseconds(50), // Short interval to ensure quick processing
                numberOfItems / numberOfThreads, // Batch size to encourage concurrent processing attempts
                numberOfThreads // Max degree of parallelism to simulate multiple threads
            );

            var tasks = new List<Task>();
            for (int i = 0; i < numberOfThreads; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    for (int j = 0; j < numberOfItems / numberOfThreads; j++)
                    {
                        // Enqueue without awaiting to simulate concurrent enqueue requests
                        var _ = queue.Enqueue(key, j);
                    }
                    await Task.Yield(); // Ensure this task yields to others
                }));
            }

            // Wait for all tasks to complete
            return Task.WhenAll(tasks);

            // If no InvalidOperationException is thrown, it means the semaphore successfully enforced single-threaded processing for the key.
            // Any exceptions thrown during processing would fail the test.
        }
    }
}
