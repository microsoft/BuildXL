// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Utils
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

        private static async Task<CopyFileResult> CopyRandomToStreamAtSpeed(CancellationToken token, Stream stream, long totalBytes, double mbPerSec)
        {
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

            return new CopyFileResult();
        }

        [Fact]
        public async Task CancellationShouldNotCauseTaskUnobservedException()
        {
            // This test checks that the bandwidth checker won't cause task unobserved exception
            // for the task provided via 'copyTaskFactory' if the CheckBandwidthAtIntervalAsync
            // is called and the operation is immediately cancelled.

            var checker = new BandwidthChecker(GetConfiguration());
            using var cts = new CancellationTokenSource();

            OperationContext context = new OperationContext(new Context(TestGlobal.Logger), cts.Token);
            int numberOfUnobservedExceptions = 0;

            EventHandler<UnobservedTaskExceptionEventArgs> taskSchedulerOnUnobservedTaskException = (o, args) => { numberOfUnobservedExceptions++; };
            try
            {
                TaskScheduler.UnobservedTaskException += taskSchedulerOnUnobservedTaskException;

                // Cancelling the operation even before starting it.
                cts.Cancel();

                // Using task completion source as an event to force the task completion in a specific time.
                var tcs = new TaskCompletionSource<object>();

                using var stream = new MemoryStream();
                var resultTask = checker.CheckBandwidthAtIntervalAsync(
                    context,
                    copyTaskFactory: token => Task.Run<CopyFileResult>(
                        async () =>
                        {
                            await tcs.Task;

                            throw new Exception("1");
                        }),
                    destinationStream: stream);

                await Task.Delay(10);
                try
                {
                    (await resultTask).IgnoreFailure();
                }
                catch (OperationCanceledException)
                {
                }

                // Triggering a failure
                tcs.SetResult(null);

                // Forcing a full GC cycle that will call all the finalizers.
                // This is important because the finalizer thread will detect tasks with unobserved exceptions.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            finally
            {
                // It is important to unsubscribe from the global event to prevent a memory leak when a test instance will stay
                // in memory indefinitely.
                TaskScheduler.UnobservedTaskException -= taskSchedulerOnUnobservedTaskException;
            }
            
            // This test is not 100% bullet proof, and it is possible that the test will pass even when the issue is still in the code.
            // But the original issue was very consistent and the test was failing even from the IDE in Debug mode all the time.
            numberOfUnobservedExceptions.Should().Be(0);
        }

        [Fact]
        public async Task BandwidthCheckDoesNotAffectGoodCopies()
        {
            var checkInterval = TimeSpan.FromSeconds(1);
            var actualBandwidthBytesPerSec = 1024;
            var actualBandwidth = MbPerSec(bytesPerSec: actualBandwidthBytesPerSec);
            var bandwidthLimit = MbPerSec(bytesPerSec: actualBandwidthBytesPerSec / 2); // Lower limit is half actual bandwidth
            var totalBytes = actualBandwidthBytesPerSec * 2;
            var checkerConfig = new BandwidthChecker.Configuration(checkInterval, bandwidthLimit, maxBandwidthLimit: null, bandwidthLimitMultiplier: null, historicalBandwidthRecordsStored: null);
            var checker = new BandwidthChecker(checkerConfig);

            using (var stream = new MemoryStream())
            {
                var result = await checker.CheckBandwidthAtIntervalAsync(_context, token => CopyRandomToStreamAtSpeed(token, stream, totalBytes, actualBandwidth), stream);
                Assert.True(result.Succeeded);
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
            var checkerConfig = new BandwidthChecker.Configuration(checkInterval, bandwidthLimit, maxBandwidthLimit: null, bandwidthLimitMultiplier: null, historicalBandwidthRecordsStored: null);
            var checker = new BandwidthChecker(checkerConfig);

            using (var stream = new MemoryStream())
            {
                var result = await checker.CheckBandwidthAtIntervalAsync(_context, token => CopyRandomToStreamAtSpeed(token, stream, totalBytes, actualBandwidth), stream);
                Assert.Equal(CopyResultCode.CopyBandwidthTimeoutError, result.Code);
            }
        }

        private static BandwidthChecker.Configuration GetConfiguration()
        {
            var checkInterval = TimeSpan.FromSeconds(1);
            var actualBandwidthBytesPerSec = 1024;
            var bandwidthLimit = MbPerSec(bytesPerSec: actualBandwidthBytesPerSec / 2); // Lower limit is half actual bandwidth
            var checkerConfig = new BandwidthChecker.Configuration(checkInterval, bandwidthLimit, maxBandwidthLimit: null, bandwidthLimitMultiplier: null, historicalBandwidthRecordsStored: null);
            return checkerConfig;
        }
    }
}
