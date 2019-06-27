// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
#endif
using System.Diagnostics.CodeAnalysis;

// disable warning regarding 'missing XML comments on public API'. We don't need docs for these methods
#pragma warning disable 1591

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <summary>
    /// Major functional areas of the entire product.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1724:TypeNamesShouldNotMatchNamespaces")]
    [SuppressMessage("Microsoft.Design", "CA1034:NestedTypesShouldNotBeVisible", Justification = "Needed by event infrastructure.")]
    public static class Tasks
    {
        // Do not use Task == 0. This results in EventSource auto-assigning a task number (to ensure inner despair).
        public const EventTask Scheduler = (EventTask)1;

        public const EventTask Parser = (EventTask)2;

        public const EventTask Storage = (EventTask)3;

        public const EventTask UnitTest = (EventTask)4;

        public const EventTask SandboxedProcessExecutor = (EventTask)5;

        public const EventTask Engine = (EventTask)6;

        public const EventTask Viewer = (EventTask)7;

        public const EventTask UnitTest2 = (EventTask)8;

        public const EventTask PipExecutor = (EventTask)9;

        public const EventTask ChangeJournalService = (EventTask)10;

        public const EventTask HostApplication = (EventTask)11;

        public const EventTask CommonInfrastructure = (EventTask)12;

        public const EventTask CacheInteraction = (EventTask)13;

        public const EventTask Debugger = (EventTask)14;

        public const EventTask Analyzers = (EventTask)15;

        public const EventTask PipInputAssertions = (EventTask)16;

        public const EventTask Distribution = (EventTask)17;

        public const EventTask CriticalPaths = (EventTask)18;

        public const EventTask ChangeDetection = (EventTask)19;

        public const EventTask Unclassified = (EventTask)20;

        public const EventTask LanguageServer = (EventTask)21;

        public const EventTask ExecutionAnalyzers = (EventTask)22;

        /// <summary>
        /// Highest-ordinal task.
        /// </summary>
        /// <remarks>
        /// This must be updated when a task is added.
        /// </remarks>
        public const EventTask Max = ExecutionAnalyzers;
    }
}
