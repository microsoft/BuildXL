// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;
using BuildXL.Tracing.CloudBuild;

namespace BuildXL.Tracing
{
    /// <summary>
    /// CloudBuild event source
    /// </summary>
    public sealed class CloudBuildEventSource : EventSource
    {
        [SuppressMessage("Microsoft.Performance", "CA1802:MarkAsConst", Justification = "If marked as const, csc complains about unreachable code")]
        private static readonly bool s_disableTargetLevelEvents = true;

        private CloudBuildEventSource(string eventSourceName)
#if NET_FRAMEWORK_451
            : base()
#else
            : base(eventSourceName, EventSourceSettings.EtwSelfDescribingEventFormat)
#endif
        {
        }

        /// <summary>
        /// Logging Instantiation
        /// </summary>
        public static CloudBuildEventSource Log => s_log;

        // $Rename: Due to telemetry backend scripts this cannot be renamed to BuildXL
        private static readonly CloudBuildEventSource s_log = new CloudBuildEventSource("CloudBuildDominoIntegration");

        /// <summary>
        /// Logging Instantiation for tests
        /// </summary>
        /// <remarks>
        /// When running drop tests in CloudBuild, Batmon should not listen to the drop events in the tests; 
        /// so we need to use a different event source name for tests.
        /// </remarks>
        public static CloudBuildEventSource TestLog => s_testLog;

        // $Rename: Due to telemetry backend scripts this cannot be renamed to BuildXL
        private static readonly CloudBuildEventSource s_testLog = new CloudBuildEventSource("CloudBuildDominoIntegration_TEST");

        /// <nodoc/>
        public void DominoInvocationEvent(DominoInvocationEvent eventObj)
        {
            WriteEvent(1, eventObj);
        }

        /// <nodoc/>
        public void DominoCompletedEvent(DominoCompletedEvent eventObj)
        {
            WriteEvent(2, eventObj);
        }

        /// <nodoc/>
        public void TargetAddedEvent(TargetAddedEvent eventObj)
        {
            if (!s_disableTargetLevelEvents)
            {
                WriteEvent(3, eventObj);
            }
        }

        /// <nodoc/>
        public void TargetRunningEvent(TargetRunningEvent eventObj)
        {
            if (!s_disableTargetLevelEvents)
            {
                WriteEvent(4, eventObj);
            }
        }

        /// <nodoc/>
        public void TargetFinishedEvent(TargetFinishedEvent eventObj)
        {
            if (!s_disableTargetLevelEvents)
            {
                WriteEvent(5, eventObj);
            }
        }

        /// <nodoc/>
        public void TargetFailedEvent(TargetFailedEvent eventObj)
        {
            if (!s_disableTargetLevelEvents)
            {
                WriteEvent(6, eventObj);
            }
        }

        /// <nodoc/>
        public void DominoContinuousStatisticsEvent(DominoContinuousStatisticsEvent eventObj)
        {
            WriteEvent(7, eventObj);
        }

        /// <nodoc/>
        public void DropCreationEvent(DropCreationEvent eventObj)
        {
            WriteEvent(8, eventObj);
        }

        /// <nodoc/>
        public void DropFinalizationEvent(DropFinalizationEvent eventObj)
        {
            WriteEvent(9, eventObj);
        }

        /// <nodoc/>
        public void DominoFinalStatisticsEvent(DominoFinalStatisticsEvent eventObj)
        {
            WriteEvent(10, eventObj);
        }
    }
}
