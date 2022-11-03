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
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    /// An attribute that defines whether <see cref="CaSaaSActivityTrackingCounters"/> counter is used by master machines only.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class MasterOnlyCounterAttribute : Attribute { }

    /// <nodoc />
    public enum CaSaaSActivityTrackingCounters
    {
        /// <nodoc />
        [MasterOnlyCounter]
        ReceivedEventHubMessages,

        /// <nodoc />
        [MasterOnlyCounter]
        ProcessedEventHubMessages,

        /// <nodoc />
        [MasterOnlyCounter]
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
        [MasterOnlyCounter]
        ProcessedMetadataRequests,
    }

    /// <summary>
    /// Tracks the key performance-related counters of this service.
    /// </summary>
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
        /// <param name="configuration">A configuration instance used for adjusting the behavior of the activity tracker.</param>
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
                _context = context.CreateNested(nameof(CacheActivityTracker));

                _activityTracker = new ActivityTracker<CaSaaSActivityTrackingCounters>(clock, configuration.CounterActivityWindow);

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

                _performanceCollector = new MachinePerformanceCollector(configuration.PerformanceCollectionFrequency, configuration.PerformanceLogWmiCounters);

                _performanceStatisticsTimer = new Timer(
                    _ => LogMachinePerformanceStatistics(),
                    state: null,
                    dueTime: configuration.PerformanceReportingPeriod,
                    period: configuration.PerformanceReportingPeriod);
            }

            /// <inheritdoc />
            public void Dispose()
            {
                try
                {
                    _snapshotTimer.Dispose();
                    _traceTimer.Dispose();
                    _performanceStatisticsTimer.Dispose();
                }
                catch (Exception e)
                {
                    Tracer.Error(_context, e, $"Failed to dispose {nameof(CacheActivityTracker)}");
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

                    if (!IsMasterMachine())
                    {
                        // Removing non-master rates
                        rates.FilterOutMasterOnlyCounters();
                    }

                    var activitySnapshot = string.Join(", ", rates.Select(kvp => $"{kvp.Key}=[{kvp.Value.ToDisplayString()}]"));
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
                    Tracer.Info(_context, $"MachinePerformanceStatistics: {machineStatistics.ToTracingString()}");

                    // Send all performance statistics for the master machine into MDM for quick dashboard interactions
                    if (IsMasterMachine())
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

            private static bool IsMasterMachine() => GlobalInfoStorage.GetGlobalInfo(GlobalInfoKey.LocalLocationStoreRole) == "Master";
        }
    }

    /// <nodoc />
    public static class CaSaaSActivityTrackingCountersExtensions
    {
        private static readonly HashSet<CaSaaSActivityTrackingCounters> _masterOnlyCounter = new ();

        static CaSaaSActivityTrackingCountersExtensions()
        {
            var enumType = typeof(CaSaaSActivityTrackingCounters);
            
            foreach (var @enum in EnumTraits<CaSaaSActivityTrackingCounters>.EnumerateValues())
            {
                if (enumType.GetField(@enum.ToString())?.IsDefined(typeof(MasterOnlyCounterAttribute), inherit: false) == true)
                {
                    _masterOnlyCounter.Add(@enum);
                }
            }
        }

        /// <summary>
        /// Returns true if the given <paramref name="counter"/> is applicable to the master machines only.
        /// </summary>
        public static bool IsMasterOnlyCounter(this CaSaaSActivityTrackingCounters counter) => _masterOnlyCounter.Contains(counter);

        /// <summary>
        /// Filters out counters specific for master machines only.
        /// </summary>
        public static void FilterOutMasterOnlyCounters(this Dictionary<CaSaaSActivityTrackingCounters, ActivityRate> activity)
        {
            foreach (var @enum in EnumTraits<CaSaaSActivityTrackingCounters>.EnumerateValues())
            {
                if (@enum.IsMasterOnlyCounter())
                {
                    activity.Remove(@enum);
                }
            }
        }
    }
}
