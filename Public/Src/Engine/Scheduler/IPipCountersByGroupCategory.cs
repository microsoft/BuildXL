// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Pips.Operations;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Interface for pip counters grouped by some category.
    /// </summary>
    public interface IPipCountersByGroupCategory
    {
        /// <summary>
        /// Increments a counter of a <see cref="Process"/>.
        /// </summary>
        void IncrementCounter(Process process, PipCountersByGroup counter);

        /// <summary>
        /// Increments a set of counters of a <see cref="Process"/>.
        /// </summary>
        void IncrementCounters(Process process, PipCountersByGroup[] counters);

        /// <summary>
        /// Adds a value to a counter of a <see cref="Process"/>.
        /// </summary>
        void AddToCounter(Process process, PipCountersByGroup counter, long value);

        /// <summary>
        /// Adds time span to a counter of a <see cref="Process"/>.
        /// </summary>
        void AddToCounter(Process process, PipCountersByGroup counter, TimeSpan timeSpan);

        /// <summary>
        /// Adds values or time spans to a set of counter of a <see cref="Process"/>.
        /// </summary>
        void AddToCounters(Process process, (PipCountersByGroup counter, long value)[] countersAndValues, (PipCountersByGroup counter, TimeSpan timeSpan)[] countersAndTimeSpans);

        /// <summary>
        /// Gets the counter value of a <see cref="Process"/>.
        /// </summary>
        /// <remarks>
        /// If there are multiple counters associated with the given <see cref="Process"/>, then the values of those counters are aggregated.
        /// </remarks>
        long GetCounterValue(Process process, PipCountersByGroup counter);

        /// <summary>
        /// Gets the counter time span of a <see cref="Process"/>.
        /// </summary>
        /// <remarks>
        /// If there are multiple counters associated with the given <see cref="Process"/>, then the time spans of those counters are aggregated.
        /// </remarks>
        TimeSpan GetElapsedTime(Process process, PipCountersByGroup counter);

        /// <summary>
        /// Put all the counters into a dictionary
        /// </summary>
        Dictionary<string, long> ToDictionarry();
    }
}
