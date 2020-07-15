// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Light-weight (allocation-free) implementation of <see cref="System.Diagnostics.Stopwatch"/>
    /// </summary>
    public readonly struct StopwatchSlim
    {
        private readonly TimeSpan m_startTimestamp;

        private StopwatchSlim(TimeSpan startTimestamp) : this()
        {
            m_startTimestamp = startTimestamp;
        }

        /// <summary>
        /// Elapsed time.
        /// </summary>
        public TimeSpan Elapsed => TimestampUtilities.Timestamp - m_startTimestamp;

        /// <summary>
        /// Starts measuring elapsed time.
        /// </summary>
        public static StopwatchSlim Start()
        {
            return new StopwatchSlim(TimestampUtilities.Timestamp);
        }
    }
}