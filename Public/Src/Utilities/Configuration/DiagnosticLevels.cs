// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Utilities.Configuration
{
    // Using has to be inside namespace due to BuildXL.Utilities.Tasks;
    using Tasks = BuildXL.Utilities.Instrumentation.Common.Tasks;

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
        PipExecutor = 1 << Tasks.PipExecutor,

        /// <summary>
        /// Input Assertions
        /// </summary>
        PipInputAssertions = 1 << Tasks.PipInputAssertions,

        /// <summary>
        /// Scheduler
        /// </summary>
        Scheduler = 1 << Tasks.Scheduler,

        /// <summary>
        /// Parser
        /// </summary>
        Parser = 1 << Tasks.Parser,

        /// <summary>
        /// Storage
        /// </summary>
        Storage = 1 << Tasks.Storage,

        /// <summary>
        /// Engine
        /// </summary>
        Engine = 1 << Tasks.Engine,

        /// <summary>
        /// Viewer
        /// </summary>
        Viewer = 1 << Tasks.Viewer,

        /// <summary>
        /// Host application
        /// </summary>
        HostApplication = 1 << Tasks.HostApplication,

        /// <summary>
        /// Common infrastructure
        /// </summary>
        CommonInfrastructure = 1 << Tasks.CommonInfrastructure,

        /// <summary>
        /// Cache interaction
        /// </summary>
        CacheInteraction = 1 << Tasks.CacheInteraction,

        /// <summary>
        /// Change journal service
        /// </summary>
        ChangeJournalService = 1 << Tasks.ChangeJournalService,

        /// <summary>
        /// The DScript debugger
        /// </summary>
        Debugger = 1 << Tasks.Debugger,

        /// <summary>
        /// Distribution
        /// </summary>
        Distribution = 1 << Tasks.Distribution,

        /// <summary>
        /// Change detection
        /// </summary>
        ChangeDetection = 1 << Tasks.ChangeDetection,

        /// <summary>
        /// Unclassified
        /// </summary>
        Unclassified = 1 << Tasks.Unclassified,

        /// <summary>
        /// Critical paths
        /// </summary>
        CriticalPaths = 1 << Tasks.CriticalPaths,
    }
}
