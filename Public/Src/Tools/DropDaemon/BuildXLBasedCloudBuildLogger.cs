// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using BuildXL.Tracing;
using BuildXL.Tracing.CloudBuild;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using Microsoft.Diagnostics.Tracing;

namespace Tool.DropDaemon
{
    /// <summary>
    ///     Implementation of <see cref="ICloudBuildLogger"/> that uses BuildXL's LoggingContext/>.
    /// </summary>
    public sealed class BuildXLBasedCloudBuildLogger : ICloudBuildLogger
    {
        private readonly ILogger m_localLogger;
        private readonly CloudBuildEventSource m_etwEventSource;

        /// <nodoc/>
        public BuildXLBasedCloudBuildLogger(ILogger logger, bool enableCloudBuildIntegration)
        {
            m_localLogger = logger;
            m_etwEventSource = enableCloudBuildIntegration ? CloudBuildEventSource.Log : CloudBuildEventSource.TestLog;
        }

        /// <inheritdoc/>
        public void Log(DropFinalizationEvent e)
        {
            LogDropEventLocally(e);
            m_etwEventSource.DropFinalizationEvent(e);
        }

        /// <inheritdoc/>
        public void Log(DropCreationEvent e)
        {
            LogDropEventLocally(e);
            m_etwEventSource.DropCreationEvent(e);
        }

        private void LogDropEventLocally(DropOperationBaseEvent e)
        {
            var enabled = BuildXL.Tracing.ETWLogger.Log.IsEnabled(EventLevel.Verbose, Keywords.CloudBuild) ? "ENABLED" : "DISABLED";
            m_localLogger.Info("Logging {0}Event(dropUrl: {1}, succeeded: {2}): {3}", e.Kind, e.DropUrl, e.Succeeded, enabled);
        }
    }
}
