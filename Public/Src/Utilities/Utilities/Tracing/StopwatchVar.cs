// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Stopwatch variable.
    /// </summary>
    public sealed class StopwatchVar
    {
        private static readonly double s_tickFrequency = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;

        /// <summary>
        /// Total elapsed time.
        /// </summary>
        public TimeSpan TotalElapsed => new TimeSpan((long)(TotalElapsedTicks * s_tickFrequency));

        /// <summary>
        /// Total elapsed ticks.
        /// </summary>
        public long TotalElapsedTicks { get; private set; }

        /// <summary>
        /// Starts measuring elapsed time.
        /// </summary>
        public StopwatchRun Start()
        {
            return new StopwatchRun(this);
        }

        /// <summary>
        /// Running stopwatch.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public readonly struct StopwatchRun : IDisposable
        {
            private readonly long m_startTimestamp;
            private readonly StopwatchVar m_stopwatch;

            /// <summary>
            /// Constructor.
            /// </summary>
            public StopwatchRun(StopwatchVar stopwatch)
            {
                m_stopwatch = stopwatch;
                m_startTimestamp = Stopwatch.GetTimestamp();
            }

            /// <summary>
            /// Elapsed ticks.
            /// </summary>
            public long ElapsedTicks => Stopwatch.GetTimestamp() - m_startTimestamp;

            /// <summary>
            /// Elapsed time.
            /// </summary>
            public TimeSpan Elapsed => new TimeSpan((long)(ElapsedTicks * s_tickFrequency));

            /// <inheritdoc />
            public void Dispose()
            {
                m_stopwatch.TotalElapsedTicks += Stopwatch.GetTimestamp() - m_startTimestamp;
            }
        }
    }
}
