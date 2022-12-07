// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    /// Helper class for converting from <see cref="CounterCollection{TEnum}"/> and <see cref="CounterSet"/>
    /// </summary>
    public static class CounterUtilities
    {
        /// <nodoc />
        public static CounterSet ToCounterSet(this CounterCollection counters)
        {
            var counterSet = new CounterSet();
            foreach ((var counter, var counterName) in counters.GetCounters())
            {
                counterSet.Add($"{counterName}.Count", counter.Value, counter.Name);

                if (counter.IsStopwatch)
                {
                    double averageMs = counter.Value != 0 ? counter.Duration.TotalMilliseconds / (double)counter.Value : 0;
                    counterSet.Add($"{counterName}.AverageMs", averageMs);
                    counterSet.Add($"{counterName}.DurationMs", counter.Duration.TotalMilliseconds);
                }
            }

            return counterSet;
        }

        /// <nodoc />
        public static CounterSet ToCounterSet(this CounterTracker counterTracker)
        {
            var result = new CounterSet();

            foreach (var counterCollection in counterTracker.CounterCollections)
            {
                result.Merge(counterCollection.ToCounterSet());
            }

            foreach ((var name, var tracker) in counterTracker.ChildCounterTrackers)
            {
                result.Merge(tracker.ToCounterSet(), name);
            }

            return result;
        }
    }
}
