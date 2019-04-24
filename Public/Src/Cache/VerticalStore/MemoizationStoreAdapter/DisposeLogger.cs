// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Logging;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// An implementation of <see cref="ILogger"/> which handles the disposal of its ILog as well.
    /// </summary>
    public sealed class DisposeLogger : ILogger
    {
        private readonly ILog m_log;
        private readonly ILogger m_logger;

        /// <summary>
        /// Creates a new of <see cref="DisposeLogger"/>
        /// </summary>
        /// <param name="logFunc">Log function</param>
        /// <param name="flushIntervalSeconds">Upper bound on how soon the logger will automatically flush if idle</param>
        public DisposeLogger(Func<ILog> logFunc, uint flushIntervalSeconds)
        {
            Contract.Requires(logFunc != null);

            m_log = logFunc();
            m_logger = flushIntervalSeconds > 0 ? new Logger(TimeSpan.FromSeconds(flushIntervalSeconds), m_log) : new Logger(m_log);
            m_logger.Info($"Started logger with flushIntervalSeconds value {flushIntervalSeconds}");
        }

        /// <inheritdoc />
        public Severity CurrentSeverity => m_logger.CurrentSeverity;

        /// <inheritdoc />
        public int ErrorCount => m_logger.ErrorCount;

        /// <inheritdoc />
        public void Always(string messageFormat, params object[] messageArgs)
        {
            m_logger.Always(messageFormat, messageArgs);
        }

        /// <inheritdoc />
        public void Debug(Exception exception)
        {
            m_logger.Debug(exception);
        }

        /// <inheritdoc />
        public void Debug(string messageFormat, params object[] messageArgs)
        {
            m_logger.Debug(messageFormat, messageArgs);
        }

        /// <inheritdoc />
        public void Diagnostic(string messageFormat, params object[] messageArgs)
        {
            m_logger.Diagnostic(messageFormat, messageArgs);
        }

        /// <inheritdoc />
        public void Error(string messageFormat, params object[] messageArgs)
        {
            m_logger.Error(messageFormat, messageArgs);
        }

        /// <inheritdoc />
        public void Error(Exception exception, string messageFormat, params object[] messageArgs)
        {
            m_logger.Error(exception, messageFormat, messageArgs);
        }

        /// <inheritdoc />
        public void ErrorThrow(Exception exception, string messageFormat, params object[] messageArgs)
        {
            m_logger.ErrorThrow(exception, messageFormat, messageArgs);
        }

        /// <inheritdoc />
        public void Fatal(string messageFormat, params object[] messageArgs)
        {
            m_logger.Fatal(messageFormat, messageArgs);
        }

        /// <inheritdoc />
        public void Flush()
        {
            m_logger.Flush();
        }

        /// <inheritdoc />
        public void Info(string messageFormat, params object[] messageArgs)
        {
            m_logger.Info(messageFormat, messageArgs);
        }

        /// <inheritdoc />
        public void Log(Severity severity, string message)
        {
            m_logger.Log(severity, message);
        }

        /// <inheritdoc />
        public void LogFormat(Severity severity, string messageFormat, params object[] messageArgs)
        {
            m_logger.LogFormat(severity, messageFormat, messageArgs);
        }

        /// <inheritdoc />
        public void Warning(string messageFormat, params object[] messageArgs)
        {
            m_logger.Warning(messageFormat, messageArgs);
        }

        private bool m_disposed;

        private void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                {
                    m_logger.Dispose();
                    m_log.Dispose();
                }

                m_disposed = true;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
    }
}
