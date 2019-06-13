// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Tracing;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Cache.ContentStore.Logging.Tracing
{
    /// <summary>
    ///     A logger class built on top of BuildXL's tracing library.
    /// </summary>
    [EventKeywordsType(typeof(Events.Keywords))]
    [EventTasksType(typeof(Events.Tasks))]
    public abstract partial class Logger
    {
        /// <summary>
        ///     Returns the logger instance
        /// </summary>
        public static Logger Log => m_log;

        /// <summary>
        ///     A generic log event for all messages from ContentStoreApp to be sent to telemetry.
        ///
        ///     The event generator is configured to be "TelemetryOnly".  When remote telemetry
        ///     is enabled (<see cref="AriaV2StaticState.Enable"/>), whenever this method is
        ///     called, a new row (containing individual arugments passed to this method) is added
        ///     to the 'contentstoreapplogmessage' table in whatever tenant Aria was enabled for.
        /// </summary>
        [GeneratedEvent(
            (ushort)EventId.ContentAddressableStoreLogMessage,
            EventGenerators = EventGenerators.TelemetryOnly,
            EventLevel = Level.Verbose,
            Message = "{dateTime} {threadId} {severity} {message}")]
        public abstract void ContentStoreAppLogMessage
            (
            LoggingContext context,
            string dateTime,
            int threadId,
            Severity severity,
            string message
            );
    }
}
