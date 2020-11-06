// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Utilities.Tasks;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Sdk;

namespace BuildXL.Cache.ContentStore.Distributed.Test.Utilities
{
    public class OrderedSemaphoreTests
    {
        private static Context Context => new Context(TestGlobal.Logger);

        [Theory]
        [InlineData(SemaphoreOrder.FIFO)]
        [InlineData(SemaphoreOrder.LIFO)]
        public async Task OrderIsRespected(SemaphoreOrder order)
        {
            var semaphore = new OrderedSemaphore(concurrencyLimit: 1, order, Context);

            var amountTasks = 10;
            var results = new List<int>();

            // Block to make sure that we have time to create all tasks before they start executing; otherwise LIFO will be flaky.
            await semaphore.WaitAsync(Timeout.InfiniteTimeSpan, CancellationToken.None);

            var tasks = Enumerable.Range(0, amountTasks).Select(async num =>
            {
                (await semaphore.WaitAsync(Timeout.InfiniteTimeSpan, CancellationToken.None)).Should().BeTrue();
                results.Add(num);
                semaphore.Release();
            }).ToArray();

            // Tasks should be blocked.
            results.Count.Should().Be(0);

            // Unblock tasks
            semaphore.Release();

            // Wait for tasks to complete and validate results.
            await Task.WhenAll(tasks);
            results.Count.Should().Be(amountTasks);
            for (var i = 0; i < results.Count; i++)
            {
                var expected = order == SemaphoreOrder.FIFO
                    ? i
                    : amountTasks - i - 1;

                results[i].Should().Be(expected);
            }
        }

        [Theory]
        [InlineData(SemaphoreOrder.FIFO)]
        [InlineData(SemaphoreOrder.LIFO)]
        [InlineData(SemaphoreOrder.NonDeterministic)]
        public async Task ConcurrencyLimitIsRespected(SemaphoreOrder order)
        {
            var cycles = 4;
            for (var cycle = 0; cycle < cycles; cycle++)
            {
                var concurrencyLimit = 10;
                var semaphore = new OrderedSemaphore(concurrencyLimit: concurrencyLimit, order, Context);

                // Use up the semaphore
                for (var i = 0; i < concurrencyLimit; i++)
                {
                    (await semaphore.WaitAsync(Timeout.InfiniteTimeSpan, CancellationToken.None)).Should().BeTrue();
                }

                // Any more should not be immediatelly successful
                var task = semaphore.WaitAsync(Timeout.InfiniteTimeSpan, CancellationToken.None);
                await Task.Delay(TimeSpan.FromMilliseconds(1));
                task.IsCompleted.Should().BeFalse();

                // Releasing at least one space should free up a space.
                semaphore.Release();
                (await task).Should().BeTrue();

                // Clear the semaphore.
                for (var i = 0; i < concurrencyLimit; i++)
                {
                    semaphore.Release();
                }
            }
        }

        [Theory]
        [InlineData(SemaphoreOrder.FIFO)]
        [InlineData(SemaphoreOrder.LIFO)]
        [InlineData(SemaphoreOrder.NonDeterministic)]
        public async Task TimeoutIsRespected(SemaphoreOrder order)
        {
            var semaphore = new OrderedSemaphore(concurrencyLimit: 0, order, Context);
            (await semaphore.WaitAsync(timeout: TimeSpan.Zero, CancellationToken.None)).Should().BeFalse();
        }

        [Theory]
        [InlineData(SemaphoreOrder.FIFO)]
        [InlineData(SemaphoreOrder.LIFO)]
        [InlineData(SemaphoreOrder.NonDeterministic)]
        public async Task LongTimeoutIsRespected(SemaphoreOrder order)
        {
            var semaphore = new OrderedSemaphore(concurrencyLimit: 0, order, Context);
            (await semaphore.WaitAsync(timeout: TimeSpan.FromMilliseconds(10), CancellationToken.None)).Should().BeFalse();
        }

        [Theory]
        [InlineData(SemaphoreOrder.FIFO)]
        [InlineData(SemaphoreOrder.LIFO)]
        [InlineData(SemaphoreOrder.NonDeterministic)]
        internal async Task AlreadyCancelledTokenIsRespected(SemaphoreOrder order)
        {
            var concurrencyLimit = 10;

            var semaphore = new OrderedSemaphore(concurrencyLimit: concurrencyLimit, order, Context);

            // Fill out the semaphore
            var tasks = Enumerable.Range(0, concurrencyLimit).Select(async num => await semaphore.WaitAsync(Timeout.InfiniteTimeSpan, CancellationToken.None));
            (await Task.WhenAll(tasks)).Should().AllBeEquivalentTo(true);

            // Queue tasks which should be blocked by other running tasks
            var cts = new CancellationTokenSource();
            cts.Cancel();
            var blockedTasks = Enumerable.Range(0, concurrencyLimit).Select(async num => await semaphore.WaitAsync(Timeout.InfiniteTimeSpan, cts.Token));

            blockedTasks.Select(t => Assert.ThrowsAsync<TaskCanceledException>(() => t)).ToArray();
        }

        [Theory]
        [InlineData(SemaphoreOrder.FIFO)]
        [InlineData(SemaphoreOrder.LIFO)]
        [InlineData(SemaphoreOrder.NonDeterministic)]
        public async Task CancellingTokenTriggersCancellation(SemaphoreOrder order)
        {
            var concurrencyLimit = 10;

            var semaphore = new OrderedSemaphore(concurrencyLimit: concurrencyLimit, order, Context);

            // Fill out the semaphore
            var tasks = Enumerable.Range(0, concurrencyLimit).Select(async num => await semaphore.WaitAsync(Timeout.InfiniteTimeSpan, CancellationToken.None));
            (await Task.WhenAll(tasks)).Should().AllBeEquivalentTo(true);

            // Queue tasks which should be blocked by other running tasks
            var cts = new CancellationTokenSource();
            var blockedTasks = Enumerable.Range(0, concurrencyLimit).Select(async num => await semaphore.WaitAsync(Timeout.InfiniteTimeSpan, cts.Token));

            await Task.Delay(TimeSpan.FromMilliseconds(1));

            // Should all be blocked
            blockedTasks.Select(t => t.IsCompleted.Should().BeFalse()).ToArray();

            cts.Cancel();

            blockedTasks.Select(t => Assert.ThrowsAsync<TaskCanceledException>(() => t)).ToArray();
        }

        [Theory]
        [InlineData(SemaphoreOrder.FIFO)]
        [InlineData(SemaphoreOrder.LIFO)]
        [InlineData(SemaphoreOrder.NonDeterministic)]
        public async Task CancellationTokenTimeoutIsDetected(SemaphoreOrder order)
        {
            var concurrencyLimit = 10;

            var semaphore = new OrderedSemaphore(concurrencyLimit: concurrencyLimit, order, Context);

            // Fill out the semaphore
            var tasks = Enumerable.Range(0, concurrencyLimit).Select(async num => await semaphore.WaitAsync(Timeout.InfiniteTimeSpan, CancellationToken.None));
            (await Task.WhenAll(tasks)).Should().AllBeEquivalentTo(true);

            // Queue tasks which should be blocked by other running tasks
            var cts = new CancellationTokenSource(delay: TimeSpan.FromMilliseconds(100));
            var blockedTasks = Enumerable.Range(0, concurrencyLimit).Select(async num => await semaphore.WaitAsync(Timeout.InfiniteTimeSpan, cts.Token));

            await Task.Delay(TimeSpan.FromMilliseconds(1));

            // Should all be blocked
            blockedTasks.Select(t => t.IsCompleted.Should().BeFalse()).ToArray();

            blockedTasks.Select(t => Assert.ThrowsAsync<TaskCanceledException>(() => t)).ToArray();
        }
    }
}
