// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
#nullable enable
namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    /// Tracks activities (like rate per second) based on <see cref="CounterCollection{TEnum}"/> instance.
    /// </summary>
    public class ActivityTracker<TEnum> where TEnum : System.Enum
    {
        private readonly IClock _clock;

        /// <summary>
        /// The timespan to determine the size of the rolling window.
        /// </summary>
        private readonly TimeSpan _averagingTimeSpan;

        private readonly object _activityLock = new object();

        private readonly List<Snapshot> _activityWindow = new List<Snapshot>();

        /// <nodoc />
        public ActivityTracker(IClock clock, TimeSpan averagingTimeSpan)
        {
            _clock = clock;
            _averagingTimeSpan = averagingTimeSpan;
        }

        /// <summary>
        /// Gets the rates.
        /// </summary>
        public Dictionary<TEnum, (long total, double ratePerSecond)> GetRates()
        {
            var result = new Dictionary<TEnum, (long total, double ratePerSecond)>();

            lock (_activityLock)
            {
                ShiftWindow(_clock.UtcNow);

                Snapshot? lastSnapshot = _activityWindow.LastOrDefault();
                Snapshot? firstSnapshot = _activityWindow.Count > 1 ? _activityWindow.First() : null;

                foreach (var v in EnumTraits<TEnum>.EnumerateValues())
                {
                    long total = lastSnapshot?.Counters[v].Value ?? 0;
                    double ratePerSecond = 0;

                    if (lastSnapshot != null && firstSnapshot != null)
                    {
                        var duration = lastSnapshot.SnapshotTime.Subtract(firstSnapshot.SnapshotTime);
                        var diff = lastSnapshot.Counters[v].Value - firstSnapshot.Counters[v].Value;
                        ratePerSecond = ((double)diff) / duration.TotalSeconds;
                    }

                    result[v] = (total, ratePerSecond);
                }
            }

            return result;
        }

        /// <summary>
        /// Clones and records a given collection.
        /// </summary>
        public void ProcessSnapshot(CounterCollection<TEnum> input)
        {
            // Snapshot clones a given input.
            var snapshot = CreateSnapshot(input);
            lock (_activityLock)
            {
                ShiftWindow(snapshot.SnapshotTime);
                _activityWindow.Add(snapshot);
            }
        }

        private void ShiftWindow(DateTime rollingWindowEndTimeUtc)
        {
            DateTime rollingWindowStartTimeUtc = rollingWindowEndTimeUtc.Subtract(_averagingTimeSpan);
            var lastValidIndex = _activityWindow.FindLastIndex(a => a.SnapshotTime < rollingWindowStartTimeUtc);

            if (lastValidIndex != -1)
            {
                _activityWindow.RemoveRange(0, lastValidIndex + 1);
            }
        }

        private Snapshot CreateSnapshot(CounterCollection<TEnum> counters)
        {
            return new Snapshot(_clock.UtcNow, counters.Clone());
        }

        private class Snapshot
        {
            public DateTime SnapshotTime { get; }
            public CounterCollection<TEnum> Counters { get; }

            public Snapshot(DateTime snapshotTime, CounterCollection<TEnum> counters)
            {
                SnapshotTime = snapshotTime;
                Counters = counters;
            }
        }
    }
}
