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
