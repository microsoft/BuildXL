// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities.Tracing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <nodoc />
    public enum CaSaaSActivityTrackingCounters
    {
        /// <nodoc />
        ReceivedEventHubMessages,

        /// <nodoc />
        ProcessedEventHubMessages,

        /// <nodoc />
        ProcessedHashes,

        /// <summary>
        /// Tracks the copy file rate.
        /// </summary>
        RemoteCopyFiles,

        /// <summary>
        /// Tracks the push file rate.
        /// </summary>
        PushFiles,

        /// <summary>
        /// Tracks the number of bytes received when copying files form other machines.
        /// </summary>
        RemoteCopyBytes,

        /// <summary>
        /// Tracks the number of bytes pushed when copying files to other machines.
        /// </summary>
        PushBytes,

        /// <summary>
        /// Tracks the number of requests processed by the metadata service.
        /// </summary>
        ProcessedMetadataRequests,
    }

    /// <summary>
    /// Tracks the key performance-related counters of this service.
    /// </summary>
    /// <remarks>
    /// This type wraps <see cref="CounterCollection{TEnum}"/> and creates the snapshots from time to time to compute event rates.
    /// Then, when <see cref="TraceCurrentSnapshot"/> is called, the activity snapshot (based on the current counters) is logged.
    /// </remarks>
    public static class CacheActivityTracker
    {
        private static readonly Tracer Tracer = new Tracer(nameof(CacheActivityTracker));

        private static object TrackerInitializationLock = new object();
        private static CacheActivityTrackerInstance? TrackerInstance;
        private static CacheActivityTrackerInstance? Tracker
        {
            get
            {
                var tracker = Volatile.Read(ref TrackerInstance);
                // The tracker can be null. Its fine because maybe it was not initialized/configured.
                return tracker;
            }
        }

        /// <summary>
        /// Starts the activity tracker.
        /// </summary>
        /// <param name="context">Tracing context.</param>
        /// <param name="clock">The clock.</param>
        /// <param name="trackingActivityWindow">A time span used for computing averages.</param>
        /// <param name="snapshotPeriod">A period when the snapshot will be collected. If null 'snapshotPeriod' will be one tenth of 'trackingActivityWindow'.</param>
        /// <param name="reportPeriod">A period hen the snapshot will be traced. If null, 'snapshotPeriod' will be used.</param>
        /// <remarks>
        /// The method must be called only once, otherwise a contract violation will occur.
        /// It is ok to call other static methods like <see cref="AddValue"/> if the method was not call at all.
        /// </remarks>
        public static void Start(Context context, IClock clock, CacheActivityTrackerConfiguration configuration)
        {
            Tracer.Info(context, "Starting activity tracker");

            bool initialized = false;
            LazyInitializer.EnsureInitialized(
                ref TrackerInstance,
                ref initialized,
                ref TrackerInitializationLock,
                () => new CacheActivityTrackerInstance(context, clock, configuration));
            Contract.Assert(initialized, "Start method should not be called more than once.");
        }

        /// <summary>
        /// Stops the activity tracker.
        /// </summary>
        public static void Stop()
        {
            var tracker = Interlocked.Exchange(ref TrackerInstance, null);
            // Its ok to call "Stop" more than once, so we don't throw/assert that 'tracker' is not null.
            tracker?.Dispose();
        }

        /// <summary>
        /// Adds a given value to a given counter.
        /// </summary>
        public static void AddValue(CaSaaSActivityTrackingCounters counter, long value)
        {
            Tracker?.Add(counter, value);
        }

        /// <summary>
        /// Increments a given counter.
        /// </summary>
        public static void Increment(CaSaaSActivityTrackingCounters counter)
        {
            Tracker?.Add(counter, 1);
        }

        private class CacheActivityTrackerInstance : IDisposable
        {
            private readonly Context _context;
            private readonly CacheActivityTrackerConfiguration _configuration;
            private readonly ActivityTracker<CaSaaSActivityTrackingCounters> _activityTracker;
            private readonly Timer _snapshotTimer;
            private readonly Timer _traceTimer;
            private readonly Timer _performanceStatisticsTimer;
            private readonly CounterCollection<CaSaaSActivityTrackingCounters> _counters = new CounterCollection<CaSaaSActivityTrackingCounters>();
            private readonly MachinePerformanceCollector _performanceCollector;

            public CacheActivityTrackerInstance(
                Context context,
                IClock clock,
                CacheActivityTrackerConfiguration configuration)
            {
                _configuration = configuration;

                _context = context.CreateNested(nameof(CacheActivityTracker));

                _activityTracker = new ActivityTracker<CaSaaSActivityTrackingCounters>(clock, _configuration.CounterActivityWindow);

                _snapshotTimer = new Timer(
                    _ => CollectSnapshot(),
                    state: null,
                    dueTime: configuration.CounterSnapshotPeriod,
                    period: configuration.CounterSnapshotPeriod);

                _traceTimer = new Timer(
                    _ => TraceSnapshot(),
                    state: null,
                    dueTime: configuration.CounterReportingPeriod,
                    period: configuration.CounterReportingPeriod);

                _performanceCollector = new MachinePerformanceCollector(_configuration.PerformanceCollectionFrequency, _configuration.PerformanceLogWmiCounters);

                _performanceStatisticsTimer = new Timer(
                    _ => LogMachinePerformanceStatistics(),
                    state: null,
                    dueTime: configuration.PerformanceReportingPeriod,
                    period: configuration.PerformanceReportingPeriod);
            }

            /// <inheritdoc />
            public void Dispose()
            {
                var exceptions = new List<Exception>(capacity: 3);
                try
                {
                    _snapshotTimer.Dispose();
                }
                catch (Exception exception)
                {
                    exceptions.Add(exception);
                }

                try
                {
                    _traceTimer.Dispose();
                }
                catch (Exception exception)
                {
                    exceptions.Add(exception);
                }

                try
                {
                    _performanceStatisticsTimer.Dispose();
                }
                catch (Exception exception)
                {
                    exceptions.Add(exception);
                }

                if (exceptions.Count > 0)
                {
                    throw new AggregateException($"Failed to dispose {nameof(CacheActivityTracker)}", exceptions);
                }
            }

            public void Add(CaSaaSActivityTrackingCounters counter, long value)
            {
                _counters[counter].Add(value);
            }

            public void CollectSnapshot()
            {
                try
                {
                    _activityTracker.ProcessSnapshot(_counters);
                }
                catch (Exception exception)
                {
                    Tracer.Error(_context, exception, "Failure processing snapshot");
                }
            }

            public void TraceSnapshot()
            {
                try
                {
                    var rates = _activityTracker.GetRates();
                    var activitySnapshot = string.Join(", ", rates.Select(kvp => $"{kvp.Key}=[total: {kvp.Value.total}, RPS: {kvp.Value.ratePerSecond:F2}]"));
                    Tracer.Info(_context, $"Activity snapshot: {activitySnapshot}");
                }
                catch (Exception exception)
                {
                    Tracer.Error(_context, exception, "Failure tracing snapshot");
                }
            }

            private void LogMachinePerformanceStatistics()
            {
                try
                {
                    var machineStatistics = _performanceCollector.GetMachinePerformanceStatistics();
                    Tracer.Info(_context, machineStatistics.ToTracingString());

                    // Send all performance statistics for the master machine into MDM for quick dashboard interactions
                    if (GlobalInfoStorage.GetGlobalInfo(GlobalInfoKey.LocalLocationStoreRole) == "Master")
                    {
                        machineStatistics.CollectMetrics((name, value) =>
                        {
                            _context.TrackMetric(name, value, "CacheMasterPerfStats");
                        });
                    }
                }
                catch (Exception exception)
                {
                    Tracer.Error(_context, exception, "Failure logging performance statistics");
                }
            }
        }
    }
}
