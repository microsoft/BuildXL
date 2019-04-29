// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Sets of pip counters that are grouped by categories.
    /// </summary>
    public class PipCountersByGroupAggregator : IPipCountersByGroupCategory
    {
        private readonly LoggingContext m_loggingContext;

        private readonly HashSet<IPipCountersByGroupCategory> m_pipCounters = new HashSet<IPipCountersByGroupCategory>();

        /// <summary>
        /// Creates an instance of <see cref="PipCountersByGroupAggregator"/>.
        /// </summary>
        public PipCountersByGroupAggregator(LoggingContext loggingContext, params IPipCountersByGroupCategory[] pipCounters)
        {
            Contract.Requires(loggingContext != null);

            m_loggingContext = loggingContext;
            m_pipCounters = new HashSet<IPipCountersByGroupCategory>(pipCounters);
        }

        /// <inheritdoc />
        public void AddToCounter(Process process, PipCountersByGroup counter, long value)
        {
            Contract.Requires(!process.IsStartOrShutdownKind, "Service pips should not attempt to add to the process pip by filters counters");

            foreach (var pipCounter in m_pipCounters)
            {
                pipCounter.AddToCounter(process, counter, value);
            }
        }

        /// <inheritdoc />
        public void AddToCounter(Process process, PipCountersByGroup counter, TimeSpan timeSpan)
        {
            Contract.Requires(!process.IsStartOrShutdownKind, "Service pips should not attempt to add to the process pip by filters counters");

            foreach (var pipCounter in m_pipCounters)
            {
                pipCounter.AddToCounter(process, counter, timeSpan);
            }
        }

        /// <inheritdoc />
        public void AddToCounters(Process process, (PipCountersByGroup counter, long value)[] countersAndValues, (PipCountersByGroup counter, TimeSpan timeSpan)[] countersAndTimeSpans)
        {
            Contract.Requires(!process.IsStartOrShutdownKind, "Service pips should not attempt to add to the process pip by filters counters");

            foreach (var pipCounter in m_pipCounters)
            {
                pipCounter.AddToCounters(process, countersAndValues, countersAndTimeSpans);
            }
        }

        /// <inheritdoc />
        public long GetCounterValue(Process process, PipCountersByGroup counter)
        {
            Contract.Requires(!process.IsStartOrShutdownKind, "Service pips should not attempt to query filters counters");

            long result = 0;

            foreach (var pipCounter in m_pipCounters)
            {
                result += pipCounter.GetCounterValue(process, counter);
            }

            return result;
        }

        /// <inheritdoc />
        public TimeSpan GetElapsedTime(Process process, PipCountersByGroup counter)
        {
            Contract.Requires(!process.IsStartOrShutdownKind, "Service pips should not attempt to query filters counters");

            TimeSpan result = TimeSpan.Zero;

            foreach (var pipCounter in m_pipCounters)
            {
                result += pipCounter.GetElapsedTime(process, counter);
            }

            return result;
        }

        /// <inheritdoc />
        public void IncrementCounter(Process process, PipCountersByGroup counter)
        {
            Contract.Requires(!process.IsStartOrShutdownKind, "Service pips should not attempt to add to the process pip by filters counters");

            foreach (var pipCounter in m_pipCounters)
            {
                pipCounter.IncrementCounter(process, counter);
            }
        }

        /// <inheritdoc />
        public void IncrementCounters(Process process, PipCountersByGroup[] counters)
        {
            Contract.Requires(!process.IsStartOrShutdownKind, "Service pips should not attempt to add to the process pip by filters counters");

            foreach (var pipCounter in m_pipCounters)
            {
                pipCounter.IncrementCounters(process, counters);
            }
        }

        /// <inheritdoc />
        public Dictionary<string, long> ToDictionarry()
        {
            var pipCounters = new Dictionary<string, long>();
            foreach (var pipCounter in m_pipCounters)
            {
                foreach (var counter in pipCounter.ToDictionarry())
                {
                    pipCounters.Add(counter.Key, counter.Value);
                }
            }
            return pipCounters;
        }

        /// <summary>
        /// log PipCounters
        /// </summary>
        public void LogAsPipCounters()
        {
            Logger.Log.PipCounters(m_loggingContext, ToDictionarry());
        }

    }
}
