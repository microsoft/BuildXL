// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.Instrumentation.Common
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
#pragma warning disable 1591

    /// <summary>
    /// This enum defines event Id's that need to be shared between components.
    /// </summary>
    public enum SharedLogEventId
    {
        None = 0,
        PipStatus = 58,
        
        StartEngineRun = 87,
        EndEngineRun = 88,
        CacheMissAnalysis = 312,
        
        [Obsolete("Please use BuildXL.Processes.Tracing.LogEvent.PipProcessError. This is only here to circumvent an inverse legacy dependency.")]
        PipProcessError = 64,

        [Obsolete("Please use BuildXL.Processes.Tracing.LogEvent.PipProcessWarning. This is only here to circumvent an inverse legacy dependency.")]
        PipProcessWarning = 65,

        CacheMissAnalysisBatchResults = 325,
        DominoInvocation = 405,
        DominoInvocationForLocalLog = 409,
        TextLogEtwOnly = 450,
        
        // This one is used by the cache to report to ETW
        CacheFileLog = 451,
        DistributionWorkerForwardedError = 7015,
        DistributionWorkerForwardedWarning = 7016,
        PipSpecifiedToRunInContainerButIsolationIsNotSupported = 12208,
        /*
         *********************************************
         * README:
         *********************************************
         *
         * Please do not add any new events in this class. 
         *
         * New events should be added to LogEvent.cs next to the Log.cs file that
         * uses the identifier. 
         *
         * The only reason for adding an event here would be if you would be using the eventid
         * from an assembly that would cause a cycle in the build graph. I.e. if Utilities needs
         * an ID reference to a log event from the Engine. But if the Engine needs the logId of
         * an event form the scheduler, you can just add a direct dependency.
         *
         *********************************************
         */
    }
}