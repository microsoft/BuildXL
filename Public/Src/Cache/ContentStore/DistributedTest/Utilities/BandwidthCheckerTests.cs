// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Sdk;

namespace BuildXL.Cache.ContentStore.Distributed.Test.Utilities
{
    public class BandwidthCheckerTests
    {
        private static readonly Random Random = new Random();

        private readonly OperationContext _context = new OperationContext(new Context(TestGlobal.Logger));

        private static double BytesPerInterval(double mbPerSecond, TimeSpan interval)
        {
            var bytesPerSecond = mbPerSecond * 1024 * 1024;
            var intervalRatio = interval.TotalSeconds;
            return bytesPerSecond * intervalRatio;
        }

        private static double MbPerSec(double bytesPerSec) => bytesPerSec / (1024 * 1024);

        private static async Task CopyRandomToStreamAtSpeed(CancellationToken token, Stream stream, long totalBytes, double mbPerSec)
        {
            //System.Diagnostics.Debugger.Launch();
            var interval = TimeSpan.FromSeconds(0.1);
            var copied = 0;
            var bytesPerInterval = (int)BytesPerInterval(mbPerSec, interval);
            Assert.True(bytesPerInterval > 0);
            var buffer = new byte[bytesPerInterval];
            while (!token.IsCancellationRequested)
            {
                var intervalTask = Task.Delay(interval);

                Random.NextBytes(buffer);
                await stream.WriteAsync(buffer, 0, bytesPerInterval);
                copied += bytesPerInterval;

                if (copied >= totalBytes)
                {
                    break;
                }

                await intervalTask;
            }
        }

        [Fact]
        public async Task BandwidthCheckDoesNotAffectGoodCopies()
        {
            var checkInterval = TimeSpan.FromSeconds(1);
            var actualBandwidthBytesPerSec = 1024;
            var actualBandwidth = MbPerSec(bytesPerSec: actualBandwidthBytesPerSec);
            var bandwidthLimit = MbPerSec(bytesPerSec: actualBandwidthBytesPerSec / 2); // Lower limit is half actual bandwidth
            var totalBytes = actualBandwidthBytesPerSec * 2;
            var checker = new BandwidthChecker(new ConstantBandwidthLimit(bandwidthLimit), checkInterval);

            using (var stream = new MemoryStream())
            {
                await checker.CheckBandwidthAtIntervalAsync(_context, token => CopyRandomToStreamAtSpeed(token, stream, totalBytes, actualBandwidth), stream);
            }
        }

        [Fact]
        public async Task BandwidthCheckTimesOutOnSlowCopy()
        {
            var checkInterval = TimeSpan.FromSeconds(1);
            var actualBandwidthBytesPerSec = 1024;
            var actualBandwidth = MbPerSec(bytesPerSec: actualBandwidthBytesPerSec);
            var bandwidthLimit = MbPerSec(bytesPerSec: actualBandwidthBytesPerSec * 2); // Lower limit is twice actual bandwidth
            var totalBytes = actualBandwidthBytesPerSec * 2;
            var checker = new BandwidthChecker(new ConstantBandwidthLimit(bandwidthLimit), checkInterval);

            using (var stream = new MemoryStream())
            {
                await Assert.ThrowsAsync(
                    typeof(TimeoutException),
                    async () => await checker.CheckBandwidthAtIntervalAsync(_context, token => CopyRandomToStreamAtSpeed(token, stream, totalBytes, actualBandwidth), stream));
            }
        }
    }
}
