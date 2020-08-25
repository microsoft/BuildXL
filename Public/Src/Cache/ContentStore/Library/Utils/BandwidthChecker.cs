// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;

#nullable enable

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Checks that a copy has a minimum bandwidth, and cancels copies otherwise.
    /// </summary>
    public class BandwidthChecker
    {
        private const double BytesInMb = 1024 * 1024;

        private readonly HistoricalBandwidthLimitSource? _historicalBandwidthLimitSource;
        private readonly IBandwidthLimitSource _bandwidthLimitSource;
        private readonly Configuration _config;

        /// <nodoc />
        public BandwidthChecker(Configuration config)
        {
            _config = config;
            _bandwidthLimitSource = config.MinimumBandwidthMbPerSec == null
                ? (IBandwidthLimitSource)new HistoricalBandwidthLimitSource(config.HistoricalBandwidthRecordsStored)
                : new ConstantBandwidthLimit(config.MinimumBandwidthMbPerSec.Value);
            _historicalBandwidthLimitSource = _bandwidthLimitSource as HistoricalBandwidthLimitSource;
        }

        /// <summary>
        /// Checks that a copy has a minimum bandwidth, and cancels it otherwise.
        /// </summary>
        /// <param name="context">The context of the operation.</param>
        /// <param name="copyTaskFactory">Function that will trigger the copy.</param>
        /// <param name="options">An option instance that controls the copy operation and allows getting the progress.</param>
        public async Task<CopyFileResult> CheckBandwidthAtIntervalAsync(OperationContext context, Func<CancellationToken, Task<CopyFileResult>> copyTaskFactory, CopyToOptions options)
        {
            if (_historicalBandwidthLimitSource != null)
            {
                var timer = Stopwatch.StartNew();
                var (result, bytesCopied) = await impl();
                timer.Stop();

                // Bandwidth checker expects speed in MiB/s, so convert it.
                var speed = bytesCopied / timer.Elapsed.TotalSeconds / BytesInMb;
                _historicalBandwidthLimitSource.AddBandwidthRecord(speed);

                return result;
            }
            else
            {
                return (await impl()).result;
            }

            async Task<(CopyFileResult result, long bytesCopied)> impl()
            {
                // This method should not fail with exceptions because the resulting task may be left unobserved causing an application to crash
                // (given that the app is configured to fail on unobserved task exceptions).
                var minimumSpeedInMbPerSec = _bandwidthLimitSource.GetMinimumSpeedInMbPerSec() * _config.BandwidthLimitMultiplier;
                minimumSpeedInMbPerSec = Math.Min(minimumSpeedInMbPerSec, _config.MaxBandwidthLimit);

                long startPosition = options.TotalBytesCopied;
                long previousPosition = startPosition;
                var copyCompleted = false;
                using var copyCancellation = CancellationTokenSource.CreateLinkedTokenSource(context.Token);

                Task<CopyFileResult> copyTask = copyTaskFactory(copyCancellation.Token);

                // Subscribing for potential task failure here to avoid unobserved task exceptions.
                traceCopyTaskFailures();

                while (!copyCompleted)
                {
                    // Wait some time for bytes to be copied
                    var firstCompletedTask = await Task.WhenAny(copyTask,
                        Task.Delay(_config.BandwidthCheckInterval, context.Token));

                    copyCompleted = firstCompletedTask == copyTask;
                    if (copyCompleted)
                    {
                        var result = await copyTask;
                        result.MinimumSpeedInMbPerSec = minimumSpeedInMbPerSec;
                        var bytesCopied = result.Size ?? options.TotalBytesCopied;

                        return (result, bytesCopied);
                    }
                    else if (context.Token.IsCancellationRequested)
                    {
                        context.Token.ThrowIfCancellationRequested();
                    }

                    // Copy is not completed and operation has not been canceled, perform
                    // bandwidth check
                    var position = options.TotalBytesCopied;
                    var receivedMiB = (position - previousPosition) / BytesInMb;
                    var currentSpeed = receivedMiB / _config.BandwidthCheckInterval.TotalSeconds;

                    if (currentSpeed == 0 || currentSpeed < minimumSpeedInMbPerSec)
                    {
                        copyCancellation.Cancel();

                        var totalBytesCopied = position - startPosition;
                        var result = new CopyFileResult(
                            CopyResultCode.CopyBandwidthTimeoutError,
                            $"Average speed was {currentSpeed}MiB/s - under {minimumSpeedInMbPerSec}MiB/s requirement. Aborting copy with {totalBytesCopied} bytes copied (received {position - previousPosition} bytes in {_config.BandwidthCheckInterval.TotalSeconds} seconds).");
                        return (result, totalBytesCopied);
                    }

                    previousPosition = position;
                }

                var copyFileResult = await copyTask;
                copyFileResult.MinimumSpeedInMbPerSec = minimumSpeedInMbPerSec;
                return (copyFileResult, previousPosition - startPosition);

                void traceCopyTaskFailures()
                {
                    // When the operation is cancelled, it is possible for the copy operation to fail.
                    // In this case we still want to trace the failure (but just with the debug severity and not with the error),
                    // but we should exclude ObjectDisposedException completely.
                    // That's why we don't use task.FireAndForget but tracing inside the task's continuation.
                    copyTask.ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                        {
                            if (!(t.Exception?.InnerException is ObjectDisposedException))
                            {
                                context.TraceDebug($"Checked copy failed. {t.Exception}");
                            }
                        }
                    });
                }
            }
        }

        /// <nodoc />
        public class Configuration
        {
            /// <nodoc />
            public Configuration(TimeSpan bandwidthCheckInterval, double? minimumBandwidthMbPerSec, double? maxBandwidthLimit, double? bandwidthLimitMultiplier, int? historicalBandwidthRecordsStored)
            {
                BandwidthCheckInterval = bandwidthCheckInterval;
                MinimumBandwidthMbPerSec = minimumBandwidthMbPerSec;
                MaxBandwidthLimit = maxBandwidthLimit ?? double.MaxValue;
                BandwidthLimitMultiplier = bandwidthLimitMultiplier ?? 1;
                HistoricalBandwidthRecordsStored = historicalBandwidthRecordsStored ?? 64;

                Contract.Assert(MaxBandwidthLimit > 0);
                Contract.Assert(BandwidthLimitMultiplier > 0);
                Contract.Assert(HistoricalBandwidthRecordsStored > 0);
            }

            /// <nodoc />
            public static readonly Configuration Default = new Configuration(TimeSpan.FromSeconds(30), null, null, null, null);

            /// <nodoc />
            public static readonly Configuration Disabled = new Configuration(TimeSpan.FromMilliseconds(int.MaxValue), minimumBandwidthMbPerSec: 0, null, null, null);

            /// <nodoc />
            public static Configuration FromDistributedContentSettings(DistributedContentSettings dcs)
            {
                if (!dcs.IsBandwidthCheckEnabled)
                {
                    return Disabled;
                }

                return new Configuration(
                    TimeSpan.FromSeconds(dcs.BandwidthCheckIntervalSeconds),
                    dcs.MinimumSpeedInMbPerSec,
                    dcs.MaxBandwidthLimit,
                    dcs.BandwidthLimitMultiplier,
                    dcs.HistoricalBandwidthRecordsStored);
            }

            /// <nodoc />
            public TimeSpan BandwidthCheckInterval { get; }

            /// <nodoc />
            public double? MinimumBandwidthMbPerSec { get; }

            /// <nodoc />
            public double MaxBandwidthLimit { get; }

            /// <nodoc />
            public double BandwidthLimitMultiplier { get; }

            /// <nodoc />
            public int HistoricalBandwidthRecordsStored { get; }
        }
    }

    /// <nodoc />
    public class BandwidthTooLowException : Exception
    {
        /// <nodoc />
        public BandwidthTooLowException(string message)
            : base(message)
        {
        }
    }
}
