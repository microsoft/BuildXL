// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.Logging.External
{
    /// <summary>
    ///     Provides an adapter between an NLog instance and our internal ILogger implementation
    /// </summary>
    public sealed class NLogAdapter : ILogger
    {
        private readonly NLog.Logger _nlog;
        private int _errorCount;

        /// <nodoc />
        public NLogAdapter(NLog.Config.LoggingConfiguration configuration)
        {
            NLog.LogManager.Configuration = configuration;
            _nlog = NLog.LogManager.GetLogger("Cache");
            NLog.LogManager.EnableLogging();

            CurrentSeverity = ComputeCurrentSeverity();
            NLog.LogManager.ConfigurationChanged += HandleConfigurationChange;
            NLog.LogManager.ConfigurationReloaded += HandleConfigurationReload;
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
            _nlog.Log(Translate(Severity.Error), messageFormat, messageArgs);
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
            NLog.LogManager.Shutdown();
        }

        private void HandleConfigurationReload(object sender, NLog.Config.LoggingConfigurationReloadedEventArgs e)
        {
            CurrentSeverity = ComputeCurrentSeverity();
        }

        private void HandleConfigurationChange(object sender, NLog.Config.LoggingConfigurationChangedEventArgs e)
        {
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
    }
}
