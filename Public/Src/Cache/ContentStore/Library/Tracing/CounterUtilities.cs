// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
                counterSet.Add($"{counterName}.Count", (long)counter.Value, counter.Name);

                if (counter.IsStopwatch)
                {
                    counterSet.Add($"{counterName}.AverageMs", counter.Value != 0 ? (long)counter.Duration.TotalMilliseconds / counter.Value : 0);
                    counterSet.Add($"{counterName}.DurationMs", (long)counter.Duration.TotalMilliseconds);
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
