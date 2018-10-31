// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Defines a counter type in a CounterCollection
    /// </summary>
    public enum CounterType
    {
        /// <summary>
        /// The counter is a number. Use IncrementCounter or AddToCounter to change it. Use GetCounterValue to get its value.
        /// </summary>
        Numeric,

        /// <summary>
        /// The counter is used to measure time. Use StartStopwatch to start measuring. Use GetElapsedTime to get the counter value.
        /// </summary>
        Stopwatch,
    }
}
