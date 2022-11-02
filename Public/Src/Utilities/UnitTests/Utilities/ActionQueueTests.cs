// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Tasks;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public class ActionQueueTests
    {
        [Fact]
        public async Task WhenQueueIsFull()
        {
            var queue = new ActionQueue(degreeOfParallelism: 2, capacityLimit: 1);
            var tcs = new TaskCompletionSource<object>();

            // Here is the idea of the test:
            // We add 3 work items (dop + 1) to make sure the queue is full.
            // and then we'll try to add another item and that item should fail.

            var t1 = queue.RunAsync(() => tcs.Task);
            
            // making sure the item was grabbed by the processor before addding the next one.
            await WaitUntilAsync(() => queue.PendingWorkItems == 0);

            var t2 = queue.RunAsync(() => tcs.Task);
            
            // making sure the item was grabbed by the processor before addding the next one.
            await WaitUntilAsync(() => queue.PendingWorkItems == 0);

            // this call will add another work item and the number of pending items will be 1.
            var t3 = queue.RunAsync(() => tcs.Task);

            await WaitUntilAsync(() => queue.PendingWorkItems == 1);

            // The queue is full for sure, the next call to run another item must fail.
            Assert.Throws<ActionBlockIsFullException>(() =>
            {
                _ = queue.RunAsync(() => { });
            });

            tcs.SetResult(null);
            
            await Task.WhenAll(t1, t2, t3);

            // Even though the task 't' is done, it still possible that the internal counter in ActionBlock was not yet decremented.
            // "waiting" until all the items are fully processed before calling 'RunAsync' to avoid 'ActionBlockIsFullException'.
            await WaitUntilAsync(() => queue.PendingWorkItems == 0);

            // should be fine now.
            await queue.RunAsync(() => { });
        }

        [Fact]
        public async Task ExceptionsShouldNotBlockProcessing()
        {
            int callbackCount = 0;
            var queue = new ActionQueue(degreeOfParallelism: 2);

            try
            {
                await queue.ForEachAsync<int>(
                    Enumerable.Range(1, 5), 
                    async (element, index) =>
                    {
                        Interlocked.Increment(ref callbackCount);
                        await Task.Yield();
                        throw new Exception($"Failing item '{element}'.");
                    }).WithTimeoutAsync(timeout: TimeSpan.FromSeconds(5));

                Assert.True(false, "ForEachAsync should fail.");
            }
            catch (TimeoutException)
            {
                Assert.True(false, "ForEachAsync got stuck");
            }
            catch (Exception e)
            {
                Assert.Equal(5, callbackCount);
                Assert.Contains("Failing item ", e.ToString());
            }
        }

        private static async Task WaitUntilAsync(Func<bool> predicate)
        {
            bool waitSucceeded = await ParallelAlgorithms.WaitUntilAsync(predicate, TimeSpan.FromMilliseconds(1), timeout: TimeSpan.FromSeconds(5));
            Assert.True(waitSucceeded);
        }
    }
}
