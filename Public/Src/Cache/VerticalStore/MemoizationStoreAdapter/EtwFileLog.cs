// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// Specialized logger which logs messages to a file and optionally also to an ETW stream which may be picked
    /// up by telemetry depending on the environment. Defaults to just logging to a <see cref="FileLog"/>.
    /// </summary>
    public sealed class EtwFileLog : FileLog
    {
        private readonly EtwOnlyTextLogger m_logger;

        /// <summary>
        /// Controls whether lines written to this <see cref="EtwFileLog"/> get emitted to ETW in addition to the file log.
        /// ETW logging is disabled by default. Disabled by default
        /// </summary>
        public static bool EnableEtwLogging { get; set; }

        /// <summary>
        /// Class constructor
        /// </summary>
        public EtwFileLog(string logFilePath, string logKind)
            : base(logFilePath)
        {
            if (EnableEtwLogging && EtwOnlyTextLogger.TryGetDefaultGlobalLoggingContext(out var loggingContext))
            {
                m_logger = new EtwOnlyTextLogger(loggingContext, logKind);
            }
        }

        /// <inheritdoc />
        public override void WriteLine(Severity severity, string severityName, string message)
        {
            m_logger?.TextLogEtwOnly((int)SharedLogEventId.CacheFileLog, severityName, message);
            base.WriteLine(severity, severityName, message);
        }
    }
}
