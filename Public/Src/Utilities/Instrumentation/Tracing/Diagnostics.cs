// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using DiagnosticTracing = System.Diagnostics.Tracing;

namespace BuildXL.Tracing
{
    /// <summary>
    /// Constants for BuildXL diagnostic tracing.
    /// </summary>
    public static class Diagnostics
    {
        /// <summary>
        /// Event level constants.
        /// </summary>
        public static class EventLevel
        {
            /// <summary>
            /// Log always.
            /// </summary>
            public const DiagnosticTracing.EventLevel LogAlways = DiagnosticTracing.EventLevel.LogAlways;

            /// <summary>
            /// Log only critical errors.
            /// </summary>
            public const DiagnosticTracing.EventLevel Critical = DiagnosticTracing.EventLevel.Critical;

            /// <summary>
            /// Log all errors, including previous levels.
            /// </summary>
            public const DiagnosticTracing.EventLevel Error = DiagnosticTracing.EventLevel.Error;

            /// <summary>
            /// Log all warnings, including previous levels.
            /// </summary>
            public const DiagnosticTracing.EventLevel Warning = DiagnosticTracing.EventLevel.Warning;

            /// <summary>
            /// Log all informational events, including previous levels
            /// </summary>
            public const DiagnosticTracing.EventLevel Informational = DiagnosticTracing.EventLevel.Informational;

            /// <summary>
            /// Log all events, including previous levels
            /// </summary>
            public const DiagnosticTracing.EventLevel Verbose = DiagnosticTracing.EventLevel.Verbose;
        }
    }
}
