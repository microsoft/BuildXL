// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using Microsoft.Extensions.Logging;

#nullable enable

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// An adapter for tracing grpc messages into the cache logging infrastructure.
    /// </summary>
    public class GrpcCacheLoggerAdapter : ILoggerFactory, ILogger
    {
        private static readonly IDisposable NoOpDisposableInstance = new NoOpDisposable();
        private readonly Tracer _tracer;
        private readonly Context _tracingContext;
        private readonly LogLevel _minLevelVerbosity;

        private class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }

        public GrpcCacheLoggerAdapter(Tracer tracer, Context tracingContext, LogLevel minLevelVerbosity)
        {
            _tracer = tracer;
            _tracingContext = tracingContext;
            _minLevelVerbosity = minLevelVerbosity;
        }

        /// <inheritdoc />
        public void AddProvider(ILoggerProvider provider)
        {
            
        }

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
        {
            return NoOpDisposableInstance;
        }

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName)
        {
            return this;
        }

        /// <inheritdoc />
        public void Dispose() { }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel)
        {
            return _minLevelVerbosity >= logLevel;
        }

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter != null)
            {
                LogToTracer(logLevel, formatter(state, exception), exception);
            }
            else
            {
                LogToTracer(logLevel, $"Id=[{eventId}]", exception);
            }
        }

        private void LogToTracer(LogLevel level, string message, Exception? exception)
        {
            switch(level)
            {
                case LogLevel.Trace:
                    _tracer.Diagnostic(_tracingContext, message);
                    break;
                case LogLevel.Debug:
                    _tracer.Debug(_tracingContext, exception, message);
                    break;
                case LogLevel.Information:
                    _tracer.Info(_tracingContext, exception, message);
                    break;
                case LogLevel.Warning:
                    _tracer.Warning(_tracingContext, exception, message);
                    break;
                case LogLevel.Error:
                case LogLevel.Critical:
                    _tracer.Error(_tracingContext, exception, message);
                    break;
            }
        }
    }
}
