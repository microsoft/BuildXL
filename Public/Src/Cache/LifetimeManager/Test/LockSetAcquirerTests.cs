// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.BlobLifetimeManager.Library;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.BlobLifetimeManager.Test
{
    public class LockSetAcquirerTests
    {
        [Fact]
        public async Task AcquisitionPausesAllTasks()
        {
            var acquirer = new LockSetAcquirer<int>(lockCount: 2);
            var resultList = new List<int>();

            var tcs = new TaskCompletionSource();
            var pendingTask = acquirer.Locks[0].UseAsync(async previousResult =>
            {
                previousResult.Should().BeNull();
                await tcs.Task;
                resultList.Add(1);
                return 1;
            }).ShouldBeSuccess();

            var semaphore = new SemaphoreSlim(0);
            var haveQueuedTask = new TaskCompletionSource();
            var acquisitionTask = acquirer.UseAsync(async results =>
            {
                // Signal the acquirer is running.
                semaphore.Release();

                // Make sure we give the next time enough time to be queued.
                await haveQueuedTask.Task;

                results.Count.Should().Be(1);
                results[0].Should().Be(1);
                resultList.Add(2);
                return 2;
            });

            var runsAfterAcquisition = Task.Run(async () =>
            {
                // Acquire the lock while the acquirer is running.
                await semaphore.WaitAsync();
                var t = acquirer.Locks[1].UseAsync(previousResult =>
                {
                    previousResult!.Value.Should().Be(2);
                    resultList.Add(3);
                    return Task.FromResult(3);
                }).ShouldBeSuccess();

                haveQueuedTask.SetResult();
                return await t;
            });

            tcs.SetResult();
            (await pendingTask).Value.Should().Be(1);
            (await acquisitionTask).Should().Be(2);
            (await runsAfterAcquisition).Value.Should().Be(3);

            (await acquirer.UseAsync(results =>
            {
                results.Count.Should().Be(2);
                results[0].Should().Be(2); // This is the previous time the acquirer ran.
                results[1].Should().Be(3);
                resultList.Add(4);
                return Task.FromResult(4);
            })).Should().Be(4);

            // If this fails, it means something is seriously wrong. This test should not be flaky.
            resultList.Should().BeEquivalentTo(new[] { 1, 2, 3, 4 });
        }

        [Fact]
        public async Task ThrownExceptionReturnsFailedResult()
        {
            var @lock = new Lock<bool>();
            var result = await @lock.UseAsync(_ => throw new Exception());
            result.Succeeded.Should().BeFalse();

            // Assert that the lock is released after an exception
            @lock.Semaphore.CurrentCount.Should().Be(1);
            (await @lock.Semaphore.WaitAsync(TimeSpan.FromMilliseconds(100))).Should().BeTrue();
            @lock.Semaphore.Release();
        }

        [Fact]
        public async Task LockSetWithFailedLockThrows()
        {
            var acquirer = new LockSetAcquirer<bool>(lockCount: 1);
            await acquirer.Locks[0].UseAsync(_ => throw new Exception()).ShouldBeError();

            var e = await Assert.ThrowsAsync<ResultPropagationException>(() => acquirer.UseAsync(_ => Task.FromResult(true)));

            // Assert that all locks are released after a failure
            foreach (var @lock in acquirer.Locks)
            {
                @lock.Semaphore.CurrentCount.Should().Be(1);
                (await @lock.Semaphore.WaitAsync(TimeSpan.FromMilliseconds(100))).Should().BeTrue();
                @lock.Semaphore.Release();
            }
        }
    }
}
