// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Utilities.Configuration
{
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
        PipExecutor = 1 << Events.Tasks.PipExecutor,

        /// <summary>
        /// Input Assertions
        /// </summary>
        PipInputAssertions = 1 << Events.Tasks.PipInputAssertions,

        /// <summary>
        /// Scheduler
        /// </summary>
        Scheduler = 1 << Events.Tasks.Scheduler,

        /// <summary>
        /// Parser
        /// </summary>
        Parser = 1 << Events.Tasks.Parser,

        /// <summary>
        /// Storage
        /// </summary>
        Storage = 1 << Events.Tasks.Storage,

        /// <summary>
        /// Engine
        /// </summary>
        Engine = 1 << Events.Tasks.Engine,

        /// <summary>
        /// Viewer
        /// </summary>
        Viewer = 1 << Events.Tasks.Viewer,

        /// <summary>
        /// Host application
        /// </summary>
        HostApplication = 1 << Events.Tasks.HostApplication,

        /// <summary>
        /// Common infrastructure
        /// </summary>
        CommonInfrastructure = 1 << Events.Tasks.CommonInfrastructure,

        /// <summary>
        /// Cache interaction
        /// </summary>
        CacheInteraction = 1 << Events.Tasks.CacheInteraction,

        /// <summary>
        /// Change journal service
        /// </summary>
        ChangeJournalService = 1 << Events.Tasks.ChangeJournalService,

        /// <summary>
        /// The DScript debugger
        /// </summary>
        Debugger = 1 << Events.Tasks.Debugger,

        /// <summary>
        /// Distribution
        /// </summary>
        Distribution = 1 << Events.Tasks.Distribution,

        /// <summary>
        /// Change detection
        /// </summary>
        ChangeDetection = 1 << Events.Tasks.ChangeDetection,

        /// <summary>
        /// Unclassified
        /// </summary>
        Unclassified = 1 << Events.Tasks.Unclassified,

        /// <summary>
        /// Critical paths
        /// </summary>
        CriticalPaths = 1 << Events.Tasks.CriticalPaths,
    }
}
