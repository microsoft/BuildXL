// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Ipc.Interfaces;
using Microsoft.Extensions.Logging;

namespace Tool.DropDaemon
{
    /// <summary>
    /// Wraps IIpcLogger as ILogger
    /// </summary>
    public class SBOMLoggingWrapper : ILogger
    {
        private readonly IIpcLogger m_innerLogger;

        /// <nodoc />
        public SBOMLoggingWrapper(IIpcLogger logger)
        {
            m_innerLogger = logger;
        }

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state) => null;

        /// <inheritdoc />
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true; // Verbosity is handled by the inner logger

        /// <inheritdoc />
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            BuildXL.Ipc.Interfaces.LogLevel innerLoggerLevel = BuildXL.Ipc.Interfaces.LogLevel.Verbose; 
            switch (logLevel)
            {
                case Microsoft.Extensions.Logging.LogLevel.Information:
                    innerLoggerLevel = BuildXL.Ipc.Interfaces.LogLevel.Info;
                    break;
                case Microsoft.Extensions.Logging.LogLevel.Warning:
                    innerLoggerLevel = BuildXL.Ipc.Interfaces.LogLevel.Warning;
                    break;
                case Microsoft.Extensions.Logging.LogLevel.Error:
                case Microsoft.Extensions.Logging.LogLevel.Critical:
                    innerLoggerLevel = BuildXL.Ipc.Interfaces.LogLevel.Error;
                    break;
                case Microsoft.Extensions.Logging.LogLevel.None:
                case Microsoft.Extensions.Logging.LogLevel.Trace:
                case Microsoft.Extensions.Logging.LogLevel.Debug:
                    // Do not log
                    return;
            }

            m_innerLogger.Log(innerLoggerLevel, $"[SBOM API] {formatter(state, exception)}");
        }
    }
}
