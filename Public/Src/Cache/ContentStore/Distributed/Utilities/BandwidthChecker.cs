// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
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
                await Impl();
                timer.Stop();
                var endPosition = destinationStream.Position;

                // Bandwidth checker expects speed in MiB/s, so convert it.
                var bytesCopied = endPosition - startPosition;
                var speed = bytesCopied / timer.Elapsed.TotalSeconds / (1024 * 1024);
                await _historicalBandwidthLimitSource.AddBandwidthRecordAsync(speed, context.Token);
            }
            else
            {
                await Impl();
            }

            async Task Impl()
            {
                // This method should not fail with exceptions because the resulting task may be left unobserved causing an application to crash
                // (given that the app is configured to fail on unobserved task exceptions).
                var minimumSpeedInMbPerSec = await _bandwidthLimitSource.GetMinimumSpeedInMbPerSecAsync(context.Token);

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
                        try
                        {
                            var position = destinationStream.Position;

                            var receivedMiB = (position - previousPosition) / BytesInMb;
                            var currentSpeed = receivedMiB / _checkInterval.TotalSeconds;
                            if (currentSpeed < minimumSpeedInMbPerSec)
                            {
                                throw new TimeoutException($"Average speed was {currentSpeed}MiB/s - under {minimumSpeedInMbPerSec}MiB/s requirement. Aborting copy with {position} copied]");
                            }

                            previousPosition = position;
                        }
                        catch (ObjectDisposedException)
                        {
                            // If the check task races with the copy completing, it might attempt to check the position of a disposed stream.
                            // Don't bother logging because the copy completed successfully.
                        }
                        catch (Exception ex)
                        {
                            var errorMessage = $"Exception thrown while checking bandwidth: {ex}";

                            // Erring on the side of caution; if something went wrong with the copy, return to avoid spin-logging the same exception.
                            // Converting TaskCanceledException to TimeoutException because the clients should know that the operation was cancelled due to timeout.
                            throw new TimeoutException(errorMessage, ex);
                        }
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
    }
}
