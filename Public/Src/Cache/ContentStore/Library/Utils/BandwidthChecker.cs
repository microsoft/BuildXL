// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Checks that a copy has a minimum bandwidth, and cancells copies otherwise.
    /// </summary>
    internal class BandwidthChecker
    {
        private const double BytesInMb = 1024 * 1024;

        private readonly HistoricalBandwidthLimitSource _historicalBandwidthLimitSource;
        private readonly IBandwidthLimitSource _bandwidthLimitSource;
        private readonly TimeSpan _checkInterval;

        /// <nodoc />
        public BandwidthChecker(IBandwidthLimitSource bandwidthLimitSource, TimeSpan checkInterval)
        {
            _bandwidthLimitSource = bandwidthLimitSource;
            _checkInterval = checkInterval;
            _historicalBandwidthLimitSource = _bandwidthLimitSource as HistoricalBandwidthLimitSource;
        }

        /// <summary>
        /// Checks that a copy has a minimum bandwidth, and cancells it otherwise.
        /// </summary>
        /// <param name="context">The context of the operation.</param>
        /// <param name="copyTaskFactory">Function that will trigger the copy.</param>
        /// <param name="destinationStream">Stream into which the copy is being made. Used to meassure bandwidth.</param>
        public async Task CheckBandwidthAtIntervalAsync(OperationContext context, Func<CancellationToken, Task> copyTaskFactory, Stream destinationStream)
        {
            if (_historicalBandwidthLimitSource != null)
            {
                var startPosition = destinationStream.Position;
                var timer = Stopwatch.StartNew();
                await impl();
                timer.Stop();
                var endPosition = destinationStream.Position;

                // Bandwidth checker expects speed in MiB/s, so convert it.
                var bytesCopied = endPosition - startPosition;
                var speed = bytesCopied / timer.Elapsed.TotalSeconds / (1024 * 1024);
                _historicalBandwidthLimitSource.AddBandwidthRecord(speed);
            }
            else
            {
                await impl();
            }

            async Task impl()
            {
                // This method should not fail with exceptions because the resulting task may be left unobserved causing an application to crash
                // (given that the app is configured to fail on unobserved task exceptions).
                var minimumSpeedInMbPerSec = _bandwidthLimitSource.GetMinimumSpeedInMbPerSec();

                long previousPosition = 0;
                var copyCompleted = false;
                using var copyCancellation = CancellationTokenSource.CreateLinkedTokenSource(context.Token);
                var copyTask = copyTaskFactory(copyCancellation.Token);

                try
                {
                    while (!copyCompleted)
                    {
                        // Wait some time for bytes to be copied
                        var firstCompletedTask = await Task.WhenAny(copyTask,
                            Task.Delay(_checkInterval, context.Token));

                        copyCompleted = firstCompletedTask == copyTask;
                        if (copyCompleted)
                        {
                            await copyTask;
                            return;
                        }
                        else if (context.Token.IsCancellationRequested)
                        {
                            context.Token.ThrowIfCancellationRequested();
                            return;
                        }

                        // Copy is not completed and operation has not been canceled, perform
                        // bandwidth check 

                        // Capture how many bytes have been copied total
                        long position = destinationStream.Position;

                        string checkResult = CheckSufficientBandwidth(position, previousPosition, minimumSpeedInMbPerSec, _checkInterval.TotalSeconds);
                        if (checkResult != null)
                        {
                            throw new TimeoutException(checkResult); // checkResult set when insufficient bandwidth found
                        }

                        // New starting point for the next time interval
                        previousPosition = position;
                    }
                }
                finally
                {
                    if (!copyCompleted)
                    {
                        // Ensure that we signal the copy to cancel
                        copyCancellation.Cancel();
                        copyTask.FireAndForget(context);
                    }
                }
            }
        }

        private static string CheckSufficientBandwidth(long position, long previousPosition, double minimumSpeedInMbPerSec, double bandwidthCheckIntervalSeconds)
        {
            double receivedMiB = (position - previousPosition) / BytesInMb;             // Calculate the total bytes of throughput in the last time interval
            double averageSpeed = receivedMiB / bandwidthCheckIntervalSeconds;          // Calculate the rate of transfer in the last time interval

            // Check whether the transfer has kept up with the minimal acceptable rate
            if (averageSpeed < minimumSpeedInMbPerSec)
            {
                string errorMessage =
                    $"Received {receivedMiB}MiB in {bandwidthCheckIntervalSeconds}s - under {minimumSpeedInMbPerSec}MiB/s requirement. Aborting copy with {position} copied]";
                return errorMessage;
            }

            // minimumSpeedInBmPerSec can be 0.
            // To prevent hangs in this case we check that the position has moved forward.
            if (previousPosition == position)
            {
                string errorMessage =
                    $"Received 0 bytes in {bandwidthCheckIntervalSeconds}s. Aborting copy with {position} copied]";
                return errorMessage;
            }

            return null;
        }
    }
}
