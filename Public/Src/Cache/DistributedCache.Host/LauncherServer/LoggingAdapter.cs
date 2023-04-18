// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using Microsoft.Extensions.Logging;

#nullable enable

namespace BuildXL.Cache.Host.Service
{
    using Severity = BuildXL.Cache.ContentStore.Interfaces.Logging.Severity;

    public sealed class LoggingAdapter : ILogger, ILoggerProvider
    {
        private readonly string _name;
        private readonly Context _context;
        private readonly LogLevel _minLogLevel;

        public LoggingAdapter(string name, Context context, LogLevel minLogLevel = LogLevel.Error) =>
            (_name, _context, _minLogLevel) = (name, context, minLogLevel);

        public IDisposable BeginScope<TState>(TState state) 
#if NET7_0_OR_GREATER
        where TState : notnull 
#endif
        => default!;

        public ILogger CreateLogger(string categoryName)
        {
            return this;
        }

        public void Dispose()
        {
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLogLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            _context.TraceMessage(GetSeverity(logLevel), message, exception,_name);
        }

        private Severity GetSeverity(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Trace:
                    break;
                case LogLevel.Debug:
                    break;
                case LogLevel.Information:
                    break;
                case LogLevel.Warning:
                    break;
                case LogLevel.Error:
                    break;
                case LogLevel.Critical:
                    break;
                case LogLevel.None:
                    break;
                default:
                    break;
            }

            return Severity.Always;
        }
    }
}
