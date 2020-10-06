// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    internal class GrpcLoggerAdapter : global::Grpc.Core.Logging.ILogger
    {
        private const string Component = "GrpcCore";

        private readonly Context _context;
        private readonly string _operation;

        public GrpcLoggerAdapter(Context context, string? operation = null)
        {
            _context = context;
            _operation = operation ?? string.Empty;
        }

        public global::Grpc.Core.Logging.ILogger ForType<T>() => new GrpcLoggerAdapter(_context, operation: typeof(T).FullName);

        public void Debug(string message) => TraceMessage(Severity.Debug, message);

        public void Debug(string format, params object[] formatArgs) => TraceMessage(Severity.Debug, format, formatArgs);

        public void Error(string message) => TraceMessage(Severity.Error, message);

        public void Error(string format, params object[] formatArgs) => TraceMessage(Severity.Error, format, formatArgs);

        public void Error(Exception exception, string message) => TraceMessage(Severity.Error, message);

        public void Info(string message) => TraceMessage(Severity.Info, message);

        public void Info(string format, params object[] formatArgs) => TraceMessage(Severity.Info, format, formatArgs);

        public void Warning(string message) => TraceMessage(Severity.Warning, message);

        public void Warning(string format, params object[] formatArgs) => TraceMessage(Severity.Warning, format, formatArgs);

        public void Warning(Exception exception, string message) => TraceMessage(Severity.Warning, message);

        private void TraceMessage(Severity severity, string message)
        {
            _context.TraceMessage(severity, message, component: Component, operation: _operation);
        }

        private void TraceMessage(Severity severity, string format, object[] formatArgs)
        {
            var message = string.Format(CultureInfo.InvariantCulture, format, formatArgs);
            _context.TraceMessage(severity, message, component: Component, operation: _operation);
        }
    }
}
