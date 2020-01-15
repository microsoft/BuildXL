// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Tracing
{
    /// <summary>
    /// Standard settings for BuildXL event generators
    /// </summary>
    public static class AriaTenantToken
    {
        /// <summary>
        /// The tenet token to use for Aria telemetry
        /// </summary>
        public const string Key = "00000000000000000000000000000000-00000000-0000-0000-0000-000000000000-0000";
    }
}
