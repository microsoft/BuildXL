// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// Exposes common members between orchestrator and worker distribution services.
    /// </summary>
    public abstract class DistributionService : IDisposable
    {
        /// <summary>
        /// Counters for message verification
        /// </summary>
        public readonly CounterCollection<DistributionCounter> Counters = new CounterCollection<DistributionCounter>();

        /// <summary>
        /// Build id to represent a distributed build session.
        /// </summary>
        public readonly string BuildId;

        /// <nodoc/>
        public DistributionService(string buildId)
        {
            BuildId = buildId;
        }

        /// <summary>
        /// Initializes the distribution service.
        /// </summary>
        /// <returns>True if initialization completed successfully. Otherwise, false.</returns>
        public abstract bool Initialize();

        /// <summary>
        /// Exits the distribution service.
        /// </summary>
        public abstract Task ExitAsync(Optional<string> failure, bool isUnexpected);

        /// <nodoc/>
        public abstract void Dispose();

        /// <summary>
        /// Log statistics of distribution counters.
        /// </summary>
        public void LogStatistics(LoggingContext loggingContext)
        {
            Counters.LogAsStatistics("Distribution", loggingContext);
        }
    }
}
