// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Cache.ContentStore.Utils;

namespace ContentStoreTest.Distributed.Redis
{
    public class RetryPolicyExtensionsTests : TestWithOutput
    {
        public RetryPolicyExtensionsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        public async Task TestTracingRetryPolicy()
        {
            var context = new OperationContext(new Context(TestGlobal.Logger));

            const int RetryCount = 3;

            var retryPolicy = RetryPolicyFactory.GetLinearPolicy(shouldRetry: _ => true, retries: RetryCount, retryInterval: TimeSpan.FromMilliseconds(1));

            int callBackCallCount = 0;
            try
            {
                await retryPolicy.ExecuteAsync(
                    context.TracingContext,
                    async () =>
                    {
                        callBackCallCount++;
                        await Task.Yield();
                        throw new ApplicationException("1");
                    },
                    CancellationToken.None,
                    databaseName: string.Empty);
                Assert.True(false, "ExecuteAsync should fail.");
            }
            catch (ApplicationException)
            {
                var fullOutput = GetFullOutput();
                fullOutput.Should().Contain($"Redis operation '{nameof(TestTracingRetryPolicy)}' failed");
                callBackCallCount.Should().Be(RetryCount + 1); // +1 because we have: original try + the number of retries.
            }
        }

        [Fact]
        public async Task RetryPolicyStopsOnCancellation()
        {
            // This test shows that if a cancellation token provided to 'RetryPolicy' is triggered
            // and at least one error already occurred, then the operation will fail and TaskCancelledException is thrown.

            var cts = new CancellationTokenSource();
            var context = new OperationContext(new Context(TestGlobal.Logger), cts.Token);

            const int RetryCount = 4;

            var retryPolicy = RetryPolicyFactory.GetLinearPolicy(shouldRetry: _ => true, retries: RetryCount, retryInterval: TimeSpan.FromMilliseconds(1));

            int callBackCallCount = 0;
            try
            {
                await retryPolicy.ExecuteAsync(
                    context.TracingContext,
                    async () =>
                    {
                        callBackCallCount++;

                        if (callBackCallCount == 2)
                        {
                            cts.Cancel();
                        }
                        await Task.Yield();
                        throw new ApplicationException($"{callBackCallCount}");
                    },
                    context.Token,
                    databaseName: string.Empty);
                Assert.True(false, "ExecuteAsync should fail.");
            }
            catch (TaskCanceledException)
            {
                callBackCallCount.Should().Be(2);
            }
        }
    }
}
