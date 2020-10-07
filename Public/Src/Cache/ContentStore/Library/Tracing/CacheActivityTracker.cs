// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
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
    }

    /// <summary>
    /// Tracks the key performance-related counters of this service.
    /// </summary>
    /// <remarks>
    /// This type wraps <see cref="CounterCollection{TEnum}"/> and creates the snapshots from time to time to compute event rates.
    /// Then, when <see cref="TraceCurrentSnapshot"/> is called, the activity snapshot (based on the current counters) is logged.
    /// </remarks>
    public class CacheActivityTracker : IDisposable
    {
        private static object _trackerInitializationLock = new object();
        private static CacheActivityTracker? _tracker;

        private static CacheActivityTracker? Tracker
        {
            get
            {
                var tracker = Volatile.Read(ref _tracker);
                // The tracker can be null. Its fine because maybe it was not initialized/configured.
                return tracker;
            }
        }

        private readonly ActivityTracker<CaSaaSActivityTrackingCounters> _activityTrackerCore;
        private readonly CounterCollection<CaSaaSActivityTrackingCounters> _counters = new CounterCollection<CaSaaSActivityTrackingCounters>();
        private readonly Timer _snapshotTimer;
        private readonly Timer _traceTimer;
        private readonly Context _context;

        /// <nodoc />
        private CacheActivityTracker(Context context, IClock clock, TimeSpan trackingActivityWindow, TimeSpan? snapshotPeriod, TimeSpan? reportPeriod)
        {
            Contract.Requires(trackingActivityWindow != TimeSpan.Zero);

            _activityTrackerCore = new ActivityTracker<CaSaaSActivityTrackingCounters>(clock, trackingActivityWindow);

            var period = snapshotPeriod ?? TimeSpan.FromSeconds(trackingActivityWindow.TotalSeconds / 10);
            _snapshotTimer = new Timer(_ => CollectSnapshotCore(), state: null, dueTime: period, period: period);

            var reportPeriodValue = reportPeriod ?? period;
            _context = context.CreateNested();
            _traceTimer = new Timer(_ => TraceCurrentSnapshot(_context), state: null, dueTime: reportPeriodValue, period: reportPeriodValue);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _context.Info("Disposing Cache activity tracker");
            _snapshotTimer.Dispose();
            _traceTimer.Dispose();
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
        public static void Start(Context context, IClock clock, TimeSpan trackingActivityWindow, TimeSpan? snapshotPeriod, TimeSpan? reportPeriod)
        {
            context.Info("Starting activity tracker");

            bool initialized = false;
            LazyInitializer.EnsureInitialized(ref _tracker, ref initialized, ref _trackerInitializationLock, () => new CacheActivityTracker(context, clock, trackingActivityWindow, snapshotPeriod, reportPeriod));
            Contract.Assert(initialized, "Start method should not be called more than once.");
        }

        /// <summary>
        /// Stops the activity tracker.
        /// </summary>
        public static void Stop()
        {
            var tracker = Interlocked.Exchange(ref _tracker, null);
            // Its ok to call "Stop" more than once, so we don't throw/assert that 'tracker' is not null.
            tracker?.Dispose();
        }

        /// <summary>
        /// Adds a given value to a given counter.
        /// </summary>
        public static void AddValue(CaSaaSActivityTrackingCounters counter, long value) => Tracker?.AddValueCore(counter, value);

        /// <summary>
        /// Increments a given counter.
        /// </summary>
        public static void Increment(CaSaaSActivityTrackingCounters counter) => Tracker?.AddValueCore(counter, 1);

        /// <summary>
        /// Create and trace current activity snapshot.
        /// </summary>
        public static void TraceCurrentSnapshot(Context context) => Tracker?.TraceActivityCore(context);

        private void AddValueCore(CaSaaSActivityTrackingCounters counter, long value) => _counters[counter].Add(value);

        private void CollectSnapshotCore() => _activityTrackerCore.ProcessSnapshot(_counters);

        private void TraceActivityCore(Context context)
        {
            var rates = _activityTrackerCore.GetRates();
            var activitySnapshot = string.Join(", ", rates.Select(kvp => $"{kvp.Key}=[total: {kvp.Value.total}, RPS: {kvp.Value.ratePerSecond:F2}]"));
            context.TraceMessage(Severity.Info, $"Activity snapshot: {activitySnapshot}", component: nameof(CacheActivityTracker));
        }

    }
}
