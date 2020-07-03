// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Utilities.Configuration
{
    // Using has to be inside namespace due to BuildXL.Utilities.Tasks;
    /// <summary>
    /// Diagnostic levels for the logging
    /// </summary>
    [Flags]
    public enum DiagnosticLevels
    {
        /// <summary>
        /// No diagnostics requested
        /// </summary>
        None = 0,

        /// <summary>
        /// Pip Execution
        /// </summary>
        PipExecutor = 1 << Instrumentation.Common.Tasks.PipExecutor,

        /// <summary>
        /// Input Assertions
        /// </summary>
        PipInputAssertions = 1 << Instrumentation.Common.Tasks.PipInputAssertions,

        /// <summary>
        /// Scheduler
        /// </summary>
        Scheduler = 1 << Instrumentation.Common.Tasks.Scheduler,

        /// <summary>
        /// Parser
        /// </summary>
        Parser = 1 << Instrumentation.Common.Tasks.Parser,

        /// <summary>
        /// Storage
        /// </summary>
        Storage = 1 << Instrumentation.Common.Tasks.Storage,

        /// <summary>
        /// Engine
        /// </summary>
        Engine = 1 << Instrumentation.Common.Tasks.Engine,

        /// <summary>
        /// Viewer
        /// </summary>
        Viewer = 1 << Instrumentation.Common.Tasks.Viewer,

        /// <summary>
        /// Host application
        /// </summary>
        HostApplication = 1 << Instrumentation.Common.Tasks.HostApplication,

        /// <summary>
        /// Common infrastructure
        /// </summary>
        CommonInfrastructure = 1 << Instrumentation.Common.Tasks.CommonInfrastructure,

        /// <summary>
        /// Cache interaction
        /// </summary>
        CacheInteraction = 1 << Instrumentation.Common.Tasks.CacheInteraction,

        /// <summary>
        /// Change journal service
        /// </summary>
        ChangeJournalService = 1 << Instrumentation.Common.Tasks.ChangeJournalService,

        /// <summary>
        /// The DScript debugger
        /// </summary>
        Debugger = 1 << Instrumentation.Common.Tasks.Debugger,

        /// <summary>
        /// Distribution
        /// </summary>
        Distribution = 1 << Instrumentation.Common.Tasks.Distribution,

        /// <summary>
        /// Change detection
        /// </summary>
        ChangeDetection = 1 << Instrumentation.Common.Tasks.ChangeDetection,

        /// <summary>
        /// Unclassified
        /// </summary>
        Unclassified = 1 << Instrumentation.Common.Tasks.Unclassified,

        /// <summary>
        /// Critical paths
        /// </summary>
        CriticalPaths = 1 << Instrumentation.Common.Tasks.CriticalPaths,
    }
}
