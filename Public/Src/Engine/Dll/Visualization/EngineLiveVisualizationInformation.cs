// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Pips;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Engine.Visualization
{
    /// <summary>
    /// A version of the visualization information that exposes the live information
    /// </summary>
    public sealed class EngineLiveVisualizationInformation : IVisualizationInformation, IDisposable
    {
        /// <summary>
        /// Constructs a new live visualization information object
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1801")]
        public EngineLiveVisualizationInformation()
        {
            // Initialize all fields
            Context = new ValueContainer<PipExecutionContext>(VisualizationValueState.NotYetAvailable);
            Configuration = new ValueContainer<IConfiguration>(VisualizationValueState.NotYetAvailable);
            MountsTable = new ValueContainer<MountsTable>(VisualizationValueState.NotYetAvailable);
            Scheduler = new ValueContainer<Scheduler.Scheduler>(VisualizationValueState.NotYetAvailable);
            PipGraph = new ValueContainer<PipGraph>(VisualizationValueState.NotYetAvailable);
            PipTable = new ValueContainer<PipTable>(VisualizationValueState.NotYetAvailable);
            LoggingContext = new ValueContainer<LoggingContext>(VisualizationValueState.NotYetAvailable);
        }

        /// <inheritdoc />
        public ValueContainer<PipExecutionContext> Context { get; }

        /// <inheritdoc />
        public ValueContainer<IConfiguration> Configuration { get; }

        /// <inheritdoc />
        public ValueContainer<MountsTable> MountsTable { get; }

        /// <inheritdoc />
        public ValueContainer<Scheduler.Scheduler> Scheduler { get; }

        /// <inheritdoc />
        public ValueContainer<PipGraph> PipGraph { get;  }

        /// <inheritdoc />
        public ValueContainer<PipTable> PipTable { get; private set; }

        /// <inheritdoc />
        public ValueContainer<LoggingContext> LoggingContext { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            if (PipTable.State == VisualizationValueState.Available)
            {
                PipTable.Value.Dispose();
            }
        }

        /// <summary>
        /// Transfer the ownership of PipTable to some other object
        /// </summary>
        public bool TransferPipTableOwnership(PipTable table)
        {
            if (PipTable.State == VisualizationValueState.Available &&
               PipTable.Value != table)
            {
                // Do not transfer because this object has a different PipTable.
                return false;
            }

            PipTable = new ValueContainer<PipTable>(VisualizationValueState.Disabled);
            return true;
        }
    }
}
