// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.ContentStore.Logging
{
    /// <summary>
    ///     Bit-bucket logger.
    /// </summary>
    public sealed class NullLogger : ILogger
    {
        private int _errorCount;

        /// <summary>
        ///     Shared default instance.
        /// </summary>
        public static readonly NullLogger Instance = new NullLogger();

        private NullLogger()
        {
        }

        /// <inheritdoc />
        public Severity CurrentSeverity => Severity.Diagnostic;

        /// <inheritdoc />
        public int ErrorCount => _errorCount;

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <inheritdoc />
        public void Flush()
        {
        }

        /// <inheritdoc />
        public void Fatal(string messageFormat, params object[] messageArgs)
        {
        }

        /// <inheritdoc />
        public void Error(string messageFormat, params object[] messageArgs)
        {
            Interlocked.Increment(ref _errorCount);
        }

        /// <inheritdoc />
        public void Error(Exception exception, string messageFormat, params object[] messageArgs)
        {
        }

        /// <inheritdoc />
        [ExcludeFromCodeCoverage]
        public void ErrorThrow(Exception exception, string messageFormat, params object[] messageArgs)
        {
            throw exception;
        }

        /// <inheritdoc />
        public void Warning(string messageFormat, params object[] messageArgs)
        {
        }

        /// <inheritdoc />
        public void Always(string messageFormat, params object[] messageArgs)
        {
        }

        /// <inheritdoc />
        public void Info(string messageFormat, params object[] messageArgs)
        {
        }

        /// <inheritdoc />
        public void Debug(string messageFormat, params object[] messageArgs)
        {
        }

        /// <inheritdoc />
        public void Debug(Exception exception)
        {
        }

        /// <inheritdoc />
        public void Diagnostic(string messageFormat, params object[] messageArgs)
        {
        }

        /// <inheritdoc />
        public void Log(Severity severity, string message)
        {
        }

        /// <inheritdoc />
        public void LogFormat(Severity severity, string messageFormat, params object[] messageArgs)
        {
        }
    }
}
