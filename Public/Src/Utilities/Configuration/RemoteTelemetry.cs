// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// How telemetry is configured
    /// </summary>
    public enum RemoteTelemetry : byte
    {
        /// <summary>
        /// Telemetry is disabled.
        /// </summary>
        Disabled,

        /// <summary>
        /// Enables telemetry. A message saying telemetry was enabled and the Sessionid will be displayed.
        /// </summary>
        EnabledAndNotify,

        /// <summary>
        /// Enables telemetry. A message saying telemetry was enabled and the Sessionid will be logged but not
        /// displayed on the console.
        /// </summary>
        EnabledAndHideNotification,
    }
}
