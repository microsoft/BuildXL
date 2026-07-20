// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Xunit;
using CloudTestClientTool = Tool.CloudTestClient.CloudTestClient;

namespace Test.Tool.CloudTestClient
{
    public class PollBackoffTests
    {
        [Fact]
        public void BackoffDoublesWithConsecutiveFailures()
        {
            var pollInterval = TimeSpan.FromSeconds(10);

            // failureCount == 1 => interval * 2^0 == interval
            Assert.Equal(TimeSpan.FromSeconds(10), CloudTestClientTool.ComputeTransientBackoff(pollInterval, 1));
            // failureCount == 2 => interval * 2^1
            Assert.Equal(TimeSpan.FromSeconds(20), CloudTestClientTool.ComputeTransientBackoff(pollInterval, 2));
            // failureCount == 3 => interval * 2^2
            Assert.Equal(TimeSpan.FromSeconds(40), CloudTestClientTool.ComputeTransientBackoff(pollInterval, 3));
            // failureCount == 4 => interval * 2^3
            Assert.Equal(TimeSpan.FromSeconds(80), CloudTestClientTool.ComputeTransientBackoff(pollInterval, 4));
        }

        [Fact]
        public void BackoffIsCappedAtMaximum()
        {
            var pollInterval = TimeSpan.FromSeconds(10);

            // A large failure count would overflow the exponential; it must saturate at the cap.
            Assert.Equal(CloudTestClientTool.MaxTransientBackoff, CloudTestClientTool.ComputeTransientBackoff(pollInterval, 100));
            // A count whose exponential exceeds the cap (10s * 2^7 == 1280s > 120s) also saturates.
            Assert.Equal(CloudTestClientTool.MaxTransientBackoff, CloudTestClientTool.ComputeTransientBackoff(pollInterval, 8));
        }
    }
}
