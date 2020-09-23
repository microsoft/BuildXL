// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Microsoft.Practices.TransientFaultHandling;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.Redis
{
    public class RetryPolicyExtensionsTests : TestWithOutput
    {
        public RetryPolicyExtensionsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task TestTracingRetryPolicy()
        {
            var context = new OperationContext(new Context(TestGlobal.Logger));

            const int RetryCount = 3;
            var retryPolicy = new RetryPolicy(
                new TransientDetectionStrategy(),
                retryCount: RetryCount,
                initialInterval: TimeSpan.FromMilliseconds(1),
                increment: TimeSpan.FromMilliseconds(1));

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
                    traceFailure: true);
                Assert.True(false, "ExecuteAsync should fail.");
            }
            catch (ApplicationException)
            {
                var fullOutput = GetFullOutput();
                fullOutput.Should().Contain($"Redis operation '{nameof(TestTracingRetryPolicy)}' failed:");
                callBackCallCount.Should().Be(RetryCount + 1); // +1 because we have: original try + the number of retries.
            }
        }

        public class TransientDetectionStrategy : ITransientErrorDetectionStrategy
        {
            public bool IsTransient(Exception ex) => true;
        }
    }
}
