// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if NET6_0_OR_GREATER

using System;
using Microsoft.Extensions.Logging;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Engine.Distribution.Grpc
{
    /// <summary>
    /// Adapter of an async file logger to pass to gRPC.NET client logging
    /// </summary>
    public class GrpcFileLoggerAdapter : ILogger, ILoggerProvider
    {
        // Using async logging to avoid making IO work synchronously on a hot path
        private readonly BuildXL.Cache.ContentStore.Logging.Logger m_logger;

        /// <nodoc />
        public GrpcFileLoggerAdapter(string path)
        {
            // Using async logging to avoid making IO work synchronously on a hot path.
            var fileLogger = new BuildXL.Cache.ContentStore.Logging.FileLog(path);
            m_logger = new BuildXL.Cache.ContentStore.Logging.Logger(synchronous: false, fileLogger);
        }

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName) => this;

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state) => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true; // Handled elsewhere

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (formatter != null)
            {
                m_logger.Log(ToSeverity(logLevel), formatter(state, exception));
            }

            if (exception != null)
            {
                m_logger.Log(ToSeverity(logLevel), exception.Message);
                m_logger.Log(ToSeverity(logLevel), exception.StackTrace);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            m_logger.Dispose();
        }

       private static BuildXL.Cache.ContentStore.Interfaces.Logging.Severity ToSeverity(LogLevel logLevel) => logLevel switch
        {
            LogLevel.Trace => BuildXL.Cache.ContentStore.Interfaces.Logging.Severity.Diagnostic,
            LogLevel.Debug => BuildXL.Cache.ContentStore.Interfaces.Logging.Severity.Debug,
            LogLevel.Information => BuildXL.Cache.ContentStore.Interfaces.Logging.Severity.Info,
            LogLevel.Warning => BuildXL.Cache.ContentStore.Interfaces.Logging.Severity.Warning,
            LogLevel.Error => BuildXL.Cache.ContentStore.Interfaces.Logging.Severity.Error,
            LogLevel.Critical => BuildXL.Cache.ContentStore.Interfaces.Logging.Severity.Fatal,
            _ => BuildXL.Cache.ContentStore.Interfaces.Logging.Severity.Unknown
        };
        
    }

}

#endif