// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Tracing;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// Specialized logger which routes log messages to telemetry as well as a log file
    /// </summary>
    public sealed class EtwFileLog : FileLog
    {
        private readonly EtwOnlyTextLogger m_logger;

        /// <summary>
        /// Class constructor
        /// </summary>
        public EtwFileLog(string logFilePath, string logKind)
            : base(logFilePath)
        {
            if (EtwOnlyTextLogger.TryGetDefaultGlobalLoggingContext(out var loggingContext))
            {
                m_logger = new EtwOnlyTextLogger(loggingContext, logKind);
            }
        }

        /// <inheritdoc />
        public override void WriteLine(Severity severity, string severityName, string message)
        {
            m_logger?.TextLogEtwOnly((int)EventId.CacheFileLog, severityName, message);
            base.WriteLine(severity, severityName, message);
        }
    }
}
