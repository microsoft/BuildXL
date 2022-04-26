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

            var t = queue.RunAsync(() => tcs.Task);
            await Assert.ThrowsAsync<ActionBlockIsFullException>(() => queue.RunAsync(() => { }));

            tcs.SetResult(null);
            
            await t;

            // Even though the task 't' is done, it still possible that the internal counter in ActionBlock was not yet decremented.
            // "waiting" until all the items are fully processed before calling 'RunAsync' to avoid 'ActionBlockIsFullException'.
            bool waitCompleted = await ParallelAlgorithms.WaitUntilAsync(() => queue.PendingWorkItems == 0, TimeSpan.FromMilliseconds(1), timeout: TimeSpan.FromSeconds(5));
            Assert.True(waitCompleted);

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
    }
}
