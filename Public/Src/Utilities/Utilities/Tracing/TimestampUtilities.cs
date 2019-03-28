// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Helpers for objects which track time. Replaces the need to use a new <see cref="Stopwatch"/> or <see cref="DateTime"/> for
    /// elapsed time measurements
    /// </summary>
    public static class TimestampUtilities
    {
        /// <summary>
        /// Shared stopwatch instance for operations which need to record elapsed time
        /// </summary>
        private static readonly Stopwatch s_stopwatch = Stopwatch.StartNew();

        /// <summary>
        /// The current timestamp as a timespan
        /// </summary>
        public static TimeSpan Timestamp => s_stopwatch.Elapsed;
    }
}
