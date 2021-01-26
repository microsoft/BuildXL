// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using BuildXL.Pips;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Logging target for events triggered when a Pip fails.
    /// </summary>
    public sealed class DumpPipLiteExecutionLogTarget : ExecutionLogTargetBase
    {
        /// <summary>
        /// Pip execution context
        /// </summary>
        private readonly PipExecutionContext m_context;

        /// <summary>
        /// Used to hydrate pips from <see cref="PipId"/>s.
        /// </summary>
        private readonly PipTable m_pipTable;

        /// <summary>
        /// Context for logging methods.
        /// </summary>
        private readonly LoggingContext m_loggingContext;

        /// <summary>
        /// Constructor
        /// </summary>
        public DumpPipLiteExecutionLogTarget(PipExecutionContext context, PipTable pipTable, LoggingContext loggingContext)
        {
            m_context = context;
            m_pipTable = pipTable;
            m_loggingContext = loggingContext;
        }

        /// <summary>
        /// Hooks into the log target for pip execution performance data which will be called
        /// when a pip fails. This will call into the dump pip lite analyzer during runtime
        /// for all failed pips.
        /// </summary>
        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            if (data.ExecutionPerformance.ExecutionLevel == PipExecutionLevel.Failed)
            {
                // TODO: Call into analyzer from here with pip once analyzer is implemented using
                //       pip id from PipExecutionPerformanceEventData
            }
        }
    }
}
