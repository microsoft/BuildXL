// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.Logging
{
    /// <summary>
    /// Allows for composing multiple <see cref="ILogger"/> into a single one, broadcasting logs to all the individual loggers
    /// </summary>
    /// <remarks>
    /// Messages intended for <see cref="IStructuredLogger"/> will be decomposed via <see cref="LoggerMessageExtensions.TraceMessage(ILogger, string, Severity, string, Exception?, string, string?)"/>
    /// and sent to <see cref="ILogger"/>
    /// </remarks>
    public class CompositeLogger : IStructuredLogger
    {
        private readonly ILogger[] _loggers;

        /// <nodoc/>
        public CompositeLogger(params ILogger[] loggers)
        {
            Contract.Requires(loggers?.Length > 0);

            _loggers = loggers;
        }

        /// <inheritdoc/>
        public Severity CurrentSeverity => _loggers.Min(logger => logger.CurrentSeverity);

        /// <inheritdoc/>
        public int ErrorCount => _loggers.Sum(logger => logger.ErrorCount);

        /// <inheritdoc/>
        public void Always(string messageFormat, params object[] messageArgs)
        {
            foreach(var logger in _loggers)
            {
                logger.Always(messageFormat, messageArgs);
            }
        }

        /// <inheritdoc/>
        public void Debug(string messageFormat, params object[] messageArgs)
        {
            foreach (var logger in _loggers)
            {
                logger.Debug(messageFormat, messageArgs);
            }
        }

        /// <inheritdoc/>
        public void Debug(Exception exception)
        {
            foreach (var logger in _loggers)
            {
                logger.Debug(exception);
            }
        }

        /// <inheritdoc/>
        public void Diagnostic(string messageFormat, params object[] messageArgs)
        {
            foreach (var logger in _loggers)
            {
                logger.Diagnostic(messageFormat, messageArgs);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (var logger in _loggers)
            {
                logger.Dispose();
            }
        }

        /// <inheritdoc/>
        public void Error(string messageFormat, params object[] messageArgs)
        {
            foreach (var logger in _loggers)
            {
                logger.Error(messageFormat, messageArgs);
            }
        }

        /// <inheritdoc/>
        public void Error(Exception exception, string messageFormat, params object[] messageArgs)
        {
            foreach (var logger in _loggers)
            {
                logger.Error(exception, messageFormat, messageArgs);
            }
        }

        /// <inheritdoc/>
        public void ErrorThrow(Exception exception, string messageFormat, params object[] messageArgs)
        {
            foreach (var logger in _loggers)
            {
                logger.ErrorThrow(exception, messageFormat, messageArgs);
            }
        }

        /// <inheritdoc/>
        public void Fatal(string messageFormat, params object[] messageArgs)
        {
            foreach (var logger in _loggers)
            {
                logger.Fatal(messageFormat, messageArgs);
            }
        }

        /// <inheritdoc/>
        public void Flush()
        {
            foreach (var logger in _loggers)
            {
                logger.Flush();
            }
        }

        /// <inheritdoc/>
        public void Info(string messageFormat, params object[] messageArgs)
        {
            foreach (var logger in _loggers)
            {
                logger.Info(messageFormat, messageArgs);
            }
        }

        /// <inheritdoc/>
        public void Log(Severity severity, string message)
        {
            foreach (var logger in _loggers)
            {
                logger.Log(severity, message);
            }
        }

        /// <inheritdoc/>
        public void Log(Severity severity, string correlationId, string message)
        {
            foreach (var logger in _loggers)
            {
                if (logger is IStructuredLogger structuredLogger)
                {
                    structuredLogger.Log(severity, correlationId, message);
                }
                else
                {
                    logger.TraceMessage(correlationId, severity, message, exception: null, component: string.Empty , operation: null);
                }
            }
        }

        /// <inheritdoc/>
        public void Log(in LogMessage logMessage)
        {
            foreach (var logger in _loggers)
            {
                if (logger is IStructuredLogger structuredLogger)
                {
                    structuredLogger.Log(logMessage);
                }
                else
                {
                    logger.TraceMessage(logMessage.OperationId, logMessage.Severity, logMessage.Message, logMessage.Exception, logMessage.TracerName, logMessage.OperationName);
                }
            }
        }

        /// <inheritdoc/>
        public void LogFormat(Severity severity, string messageFormat, params object[] messageArgs)
        {
            foreach (var logger in _loggers)
            {
                logger.LogFormat(severity, messageFormat, messageArgs);
            }
        }

        /// <inheritdoc/>
        public void LogOperationFinished(in OperationResult result)
        {
            foreach (var logger in _loggers)
            {
                if (logger is IStructuredLogger structuredLogger)
                {
                    structuredLogger.LogOperationFinished(result);
                }
                else
                {
                    logger.TraceMessage(result.OperationId, result.Severity, result.Message, exception: null, result.TracerName, result.OperationName);
                }
            }
        }

        /// <inheritdoc/>
        public void LogOperationStarted(in OperationStarted operation)
        {
            foreach (var logger in _loggers)
            {
                if (logger is IStructuredLogger structuredLogger)
                {
                    structuredLogger.LogOperationStarted(operation);
                }
                else
                {
                    logger.TraceMessage(operation.OperationId, operation.Severity, operation.Message, exception: null, operation.TracerName, operation.OperationName);
                }
            }
        }

        /// <inheritdoc/>
        public void Warning(string messageFormat, params object[] messageArgs)
        {
            foreach (var logger in _loggers)
            {
                logger.Warning(messageFormat, messageArgs);
            }
        }
    }
}
