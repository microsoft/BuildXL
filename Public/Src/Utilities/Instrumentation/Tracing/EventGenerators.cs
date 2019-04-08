// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Tracing
{
    /// <summary>
    /// Standard settings for BuildXL event generators
    /// </summary>
    public static class EventGenerators
    {
        /// <summary>
        /// Enables local and telemetry generators
        /// </summary>
        public const Generators LocalAndTelemetry = LocalOnly | TelemetryOnly;

        /// <summary>
        /// Events that are only logged locally but all of them are inspectable.
        /// </summary>
        public const Generators LocalOnly = Generators.ManifestedEventSource | Generators.InspectableLogger;

        /// <summary>
        /// Events that only get set to telemetry
        /// </summary>
        public const Generators TelemetryOnly = Generators.AriaV2;

        /// <summary>
        /// Enables local, telemetry, and Statistics generators
        /// </summary>
        public const Generators LocalAndTelemetryAndStatistic = LocalAndTelemetry | Generators.Statistics;

        /// <summary>
        /// The RV name for AriaV1 telemetry
        /// </summary>
        // $Rename: Due to telemetry backend scripts this cannot be renamed to BuildXL
        public const string RvName = "tse_Domino";
    }
}
