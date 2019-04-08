// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    /// Lighter-weight implementation of <see cref="System.Diagnostics.Stopwatch"/>
    /// </summary>
    public readonly struct StopwatchSlim
    {
        private readonly TimeSpan _startTimestamp;

        private StopwatchSlim(TimeSpan startTimestamp) : this()
        {
            _startTimestamp = startTimestamp;
        }

        /// <summary>
        /// Elapsed time.
        /// </summary>
        public TimeSpan Elapsed => TimestampUtilities.Timestamp - _startTimestamp;

        /// <summary>
        /// Starts measuring elapsed time.
        /// </summary>
        public static StopwatchSlim Start()
        {
            return new StopwatchSlim(TimestampUtilities.Timestamp);
        }
    }
}
