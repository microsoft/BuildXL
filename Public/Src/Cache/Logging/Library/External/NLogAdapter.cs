// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.Logging.External
{
    /// <summary>
    ///     Provides an adapter between an NLog instance and our internal ILogger implementation
    /// </summary>
    public sealed class NLogAdapter : ILogger, IStructuredLogger
    {
        private readonly ILogger _host;
        private readonly NLog.Logger _nlog;
        private int _errorCount;

        /// <summary>
        /// Used to prevent double-dispose from happening
        /// </summary>
        private int _disposed = 0;

        /// <summary>
        /// Whether to set unobserved task exceptions as observed.
        /// </summary>
        public bool ObserveUnobservedTaskExceptions { get; set; } = true;

        /// <nodoc />
        public NLogAdapter(ILogger logger, NLog.Config.LoggingConfiguration configuration)
        {
            Contract.RequiresNotNull(logger);
            Contract.RequiresNotNull(configuration);

            _host = logger;

            NLog.LogManager.Configuration = configuration;
            _nlog = NLog.LogManager.GetLogger("Cache");
            NLog.LogManager.EnableLogging();

            CurrentSeverity = ComputeCurrentSeverity();

            // Hook configuration readers up to config change events
            NLog.LogManager.ConfigurationChanged += HandleConfigurationChange;
            NLog.LogManager.ConfigurationReloaded += HandleConfigurationReload;

            // Hook log cleanup up to exit handlers.
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            AppDomain.CurrentDomain.DomainUnload += CurrentDomain_DomainUnload;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        /// <inheritdoc />
        public Severity CurrentSeverity { get; private set; }

        /// <inheritdoc />
        public int ErrorCount => _errorCount;

        /// <inheritdoc />
        public void Debug(string messageFormat, params object[] messageArgs)
        {
            _nlog.Log(Translate(Severity.Debug), messageFormat, messageArgs);
        }

        /// <inheritdoc />
        public void Debug(Exception exception)
        {
            _nlog.Log(Translate(Severity.Debug), exception);
        }

        /// <inheritdoc />
        public void Diagnostic(string messageFormat, params object[] messageArgs)
        {
            _nlog.Log(Translate(Severity.Diagnostic), messageFormat, messageArgs);
        }

        /// <inheritdoc />
        public void Info(string messageFormat, params object[] messageArgs)
        {
            _nlog.Log(Translate(Severity.Info), messageFormat, messageArgs);
        }

        /// <inheritdoc />
        public void Warning(string messageFormat, params object[] messageArgs)
        {
            _nlog.Log(Translate(Severity.Warning), messageFormat, messageArgs);
        }

        /// <inheritdoc />
        public void Error(string messageFormat, params object[] messageArgs)
        {
            _nlog.Log(Translate(Severity.Error), messageFormat, messageArgs);
            Interlocked.Increment(ref _errorCount);
        }

        /// <inheritdoc />
        public void Error(Exception exception, string messageFormat, params object[] messageArgs)
        {
            _nlog.Log(Translate(Severity.Error), exception, messageFormat, messageArgs);
            Interlocked.Increment(ref _errorCount);
        }

        /// <inheritdoc />
        public void ErrorThrow(Exception exception, string messageFormat, params object[] messageArgs)
        {
            _nlog.Log(Translate(Severity.Error), exception, messageFormat, messageArgs);
            Interlocked.Increment(ref _errorCount);
        }

        /// <inheritdoc />
        public void Fatal(string messageFormat, params object[] messageArgs)
        {
            _nlog.Log(Translate(Severity.Fatal), messageFormat, messageArgs);
        }

        /// <inheritdoc />
        public void Always(string messageFormat, params object[] messageArgs)
        {
            _nlog.Log(Translate(Severity.Always), messageFormat, messageArgs);
        }

        /// <inheritdoc />
        public void Log(Severity severity, string message)
        {
            _nlog.Log(Translate(severity), message);

            if (severity == Severity.Error)
            {
                Interlocked.Increment(ref _errorCount);
            }
        }

        /// <inheritdoc />
        public void Log(Severity severity, string correlationId, string message)
        {
            var logLine = new NLog.LogEventInfo(level: Translate(severity), loggerName: null, message: message);
            logLine.Properties[MetaData.CorrelationId] = correlationId;

            _nlog.Log(logLine);

            if (severity == Severity.Error)
            {
                Interlocked.Increment(ref _errorCount);
            }
        }

        /// <inheritdoc />
        public void LogOperationFinished(in OperationResult result)
        {
            var severity = result.Severity;

            var logLine = new NLog.LogEventInfo(level: Translate(severity), loggerName: null, message: result.Message);
            logLine.Exception = result.Exception;
            logLine.Properties[MetaData.CorrelationId] = result.OperationId;
            logLine.Properties[MetaData.OperationName] = result.OperationName;
            logLine.Properties[MetaData.OperationComponent] = result.TracerName;
            // Arguments can take arbitrary information. It should be space separated
            // key values.
            logLine.Properties[MetaData.OperationArguments] = "Kind=" + result.OperationKind;
            logLine.Properties[MetaData.OperationResult] = result.Status;
            logLine.Properties[MetaData.OperationDuration] = result.Duration;

            _nlog.Log(logLine);

            if (severity == Severity.Error)
            {
                Interlocked.Increment(ref _errorCount);
            }
        }

        /// <inheritdoc />
        public void LogFormat(Severity severity, string messageFormat, params object[] messageArgs)
        {
            _nlog.Log(Translate(severity), messageFormat, messageArgs);

            if (severity == Severity.Error)
            {
                Interlocked.Increment(ref _errorCount);
            }
        }

        /// <inheritdoc />
        public void Flush()
        {
            NLog.LogManager.Flush();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // The disposed field is about protecting from a double-dispose issue that can happen when shutting down,
            // even if we don't have any errors. The CompareExchange is merely a mechanism 
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
#pragma warning disable CA1031 // Do not catch general exception types
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                try
                {
                    _host.Info("Disposing NLog instance");
                }
                catch (Exception)
                {
                }

                try
                {
                    Flush();
                    NLog.LogManager.Shutdown();
                }
                catch (Exception)
                {
                }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
#pragma warning restore CA1031 // Do not catch general exception types
            }
        }

        private void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            Dispose();
        }

        private void CurrentDomain_DomainUnload(object sender, EventArgs e)
        {
            Dispose();
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            var exception = args.ExceptionObject as Exception;

            string exitString = args.IsTerminating
                                    ? "Process will exit"
                                    : "Process will not exit";

            try
            {
                _host.Error(exception, "An exception has occured and is unhandled. {0}", exitString);
            }
            catch (Exception loggerException)
            {
                Console.WriteLine("Logger threw exception: {0} while trying to log unhandled exception {1}. {2}",
                                  loggerException,
                                  exception ?? args.ExceptionObject,
                                  exitString);
            }
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs args)
        {
            var exception = args.Exception;
            var exceptionObserver = new TaskExceptionObserver();

            try
            {
                if (!exceptionObserver.IsWellKnownException(exception))
                {
                    _nlog.Log(Translate(Severity.Warning), "Exception has occurred in an unobserved task. Process may exit. Exception=[{0}]", exception);
                }
            }
            catch (Exception nLogException)
            {
                try
                {
                    _host.Warning("NLog threw exception: {0} while trying to log an unobserved task exception: {1}", nLogException, exception);
                }
                catch (Exception hostException)
                {
                    Console.WriteLine("NLog threw exception: {0} Host logger threw exception: {1} while trying to log an unobserved task exception: {2}",
                        nLogException,
                        hostException,
                        exception);
                }
            }

            if (ObserveUnobservedTaskExceptions && exceptionObserver.IsWellKnownException(exception) && !args.Observed)
            {
                args.SetObserved();
            }
        }

        private void HandleConfigurationReload(object sender, NLog.Config.LoggingConfigurationReloadedEventArgs e)
        {
            if (e.Succeeded)
            {
                _host.Info("NLog configuration has been reloaded successfully");
            }
            else
            {
                _host.Error(e.Exception, "Attempted NLog configuration reload failed");
            }

            CurrentSeverity = ComputeCurrentSeverity();
        }

        private void HandleConfigurationChange(object sender, NLog.Config.LoggingConfigurationChangedEventArgs e)
        {
            _host.Info("NLog configuration has changed");

            CurrentSeverity = ComputeCurrentSeverity();
        }

        private Severity ComputeCurrentSeverity()
        {
            if (_nlog.IsTraceEnabled)
            {
                return Severity.Diagnostic;
            }

            if (_nlog.IsDebugEnabled)
            {
                return Severity.Debug;
            }

            if (_nlog.IsInfoEnabled)
            {
                return Severity.Info;
            }

            if (_nlog.IsWarnEnabled)
            {
                return Severity.Warning;
            }

            if (_nlog.IsErrorEnabled)
            {
                return Severity.Error;
            }

            if (_nlog.IsFatalEnabled)
            {
                return Severity.Fatal;
            }

            return Severity.Unknown;
        }

        private static NLog.LogLevel Translate(Severity severity)
        {
            return severity switch
            {
                Severity.Unknown => NLog.LogLevel.Off,
                Severity.Diagnostic => NLog.LogLevel.Trace,
                Severity.Debug => NLog.LogLevel.Debug,
                Severity.Info => NLog.LogLevel.Info,
                Severity.Warning => NLog.LogLevel.Warn,
                Severity.Error => NLog.LogLevel.Error,
                Severity.Fatal => NLog.LogLevel.Fatal,
                // Notice that we can loose Always traces because there is no such concept in NLog
                Severity.Always => NLog.LogLevel.Info,
                _ => throw new NotImplementedException("Missing log level translation"),
            };
        }

        /// <summary>
        /// Contains information about extra columns emitted by the logger.
        /// </summary>
        /// <remarks>
        /// The names here should match the layout of NLog.config file.
        /// </remarks>
        public class MetaData
        {
            /// <nodoc />
            public const string CorrelationId = nameof(CorrelationId);

            /// <nodoc />
            public const string OperationComponent = nameof(OperationComponent);

            /// <nodoc />
            public const string OperationName = nameof(OperationName);

            /// <nodoc />
            public const string OperationArguments = nameof(OperationArguments);

            /// <nodoc />
            public const string OperationResult = nameof(OperationResult);

            /// <nodoc />
            public const string OperationDuration = nameof(OperationDuration);

            /// <nodoc />
            public const string OperationException = nameof(OperationException);
        }
    }
}
