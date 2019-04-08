// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
