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
        [InlineData(true)]
        [InlineData(false)]
        public async Task TestTracingRetryPolicy(bool usePolly)
        {
            RetryPolicyFactory.UsePolly = usePolly;

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

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task RetryPolicyStopsOnCancellation(bool usePolly)
        {
            RetryPolicyFactory.UsePolly = usePolly;

            // This test shows that if a cancellation token provided to 'RetryPolicy' is triggered
            // and at least one error already occurred, then the operation will fail with the last exception
            // If using Polly, TaskCancelledException is thrown.

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
            catch (ApplicationException e)
            {
                usePolly.Should().BeFalse();
                callBackCallCount.Should().Be(2);
                e.Message.Should().Be("2");
            }
            catch (TaskCanceledException)
            {
                usePolly.Should().BeTrue();
                callBackCallCount.Should().Be(2);
            }
        }
    }
}
