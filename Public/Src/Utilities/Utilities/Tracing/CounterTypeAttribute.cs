// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Attribute used to assign counter types to the base enum values for CounterCollection
    /// </summary>
    [AttributeUsage(AttributeTargets.Enum | AttributeTargets.Field)]
    public sealed class CounterTypeAttribute : Attribute
    {
        /// <summary>
        /// The counter type associated with a given member
        /// </summary>
        public CounterType CounterType { get; }

        /// <summary>
        /// Optional name of the metric. This is only set if we wish the counter to be logged as a top-level metric
        /// </summary>
        public string CounterName { get; }

        /// <summary>
        /// The attribute constructor
        /// </summary>
        public CounterTypeAttribute(CounterType counterType, string counterName = null)
        {
            CounterType = counterType;
            CounterName = counterName;
        }
    }
}
