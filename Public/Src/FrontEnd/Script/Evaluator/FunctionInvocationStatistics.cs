// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// Tracks statistics of methods calls from the evaluation engine.
    /// </summary>
    public sealed class FunctionInvocationStatistics
    {
        private readonly ConcurrentQueue<FunctionStatistic> m_counterNameToCounterIdMap = new ConcurrentQueue<FunctionStatistic>();

        /// <summary>
        /// Returns <paramref name="count"/> most called methods.
        /// </summary>
        public InvocationStatistics[] GetTopCounters(int count, StringTable stringTable)
        {
            // Top n longest methods
            var longest =
                m_counterNameToCounterIdMap.Select(c => new InvocationStatistics(c.FullName, c.Occurrences, c.Elapsed))
                    .OrderByDescending(k => k.Duration)
                    .Take(count)
                    .ToArray();

            // Top n most frequent methods.
            var mostFrequent = m_counterNameToCounterIdMap.Select(c => new InvocationStatistics(c.FullName, c.Occurrences, c.Elapsed))
                    .OrderByDescending(k => k.Count)
                    .Take(count)
                    .ToArray();

            return longest.Union(mostFrequent).ToArray();
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public readonly struct InvocationStatistics
        {
            /// <nodoc />
            public string MethodName { get; }

            /// <nodoc />
            public long Count { get; }

            /// <nodoc />
            public TimeSpan Duration { get; }

            /// <nodoc />
            public InvocationStatistics(string methodName, long count, long duration)
            {
                MethodName = methodName;
                Count = count;
                Duration = TimeSpan.FromTicks(duration);
            }
        }

        /// <nodoc />
        public void Add(FunctionStatistic functionStatistic)
        {
            m_counterNameToCounterIdMap.Enqueue(functionStatistic);
        }
    }
}
