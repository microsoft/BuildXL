// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Engine.Visualization
{
    /// <summary>
    /// Interface that interacts with the information needed from the engine
    /// </summary>
    public interface IVisualizationInformation
    {
        /// <summary>
        /// Access to the Context that holds StringTable, PathTable and other usefull tables.
        /// </summary>
        ValueContainer<PipExecutionContext> Context { get; }

        /// <summary>
        /// Access to the Configuration object
        /// </summary>
        ValueContainer<IConfiguration> Configuration { get; }

        /// <summary>
        /// Access to the mounts table
        /// </summary>
        ValueContainer<MountsTable> MountsTable { get; }

        /// <summary>
        /// Access to the Scheduler
        /// </summary>
        ValueContainer<Scheduler.Scheduler> Scheduler { get; }

        /// <summary>
        /// Access to the pip graph
        /// </summary>
        ValueContainer<PipGraph> PipGraph { get; }

        /// <summary>
        /// Access to the PipTable
        /// </summary>
        ValueContainer<PipTable> PipTable { get; }

        /// <summary>
        /// Access to the LoggingContext
        /// </summary>
        ValueContainer<LoggingContext> LoggingContext { get; }
    }
}
