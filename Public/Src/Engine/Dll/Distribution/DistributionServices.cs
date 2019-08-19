// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// Verifies bond messages
    /// </summary>
    public sealed class DistributionServices
    {
        /// <summary>
        /// Counters for message verification
        /// </summary>
        public readonly CounterCollection<DistributionCounter> Counters = new CounterCollection<DistributionCounter>();

        /// <summary>
        /// Build id to represent a distributed build session.
        /// </summary>
        public string BuildId { get; }

        /// <nodoc/>
        public DistributionServices(string buildId)
        {
            BuildId = buildId;
        }

        /// <summary>
        /// Log statistics of distribution counters.
        /// </summary>
        public void LogStatistics(LoggingContext loggingContext)
        {
            Counters.LogAsStatistics("Distribution", loggingContext);
        }
    }
}
