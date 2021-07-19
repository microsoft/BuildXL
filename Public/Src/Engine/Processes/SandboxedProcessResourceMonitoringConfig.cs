// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// A configuration indicating if resource usage monitoring is enabled when sandboxing
    /// and at what interval it should happen for the whole sandboxed process tree.
    /// </summary>
    public class SandboxedProcessResourceMonitoringConfig
    {
        /// <summary>
        /// Indicates if monitoring is enabled.
        /// </summary>
        public bool MonitoringEnabled { get; }

        /// <summary>
        /// Indicates at what frequency resource usage snapshots should be taken.
        /// </summary>
        public TimeSpan RefreshInterval { get; }

        /// <nodoc />
        public SandboxedProcessResourceMonitoringConfig(bool enabled, TimeSpan refreshInterval)
        {
            MonitoringEnabled = enabled;
            RefreshInterval = refreshInterval;
        }
    }
}
