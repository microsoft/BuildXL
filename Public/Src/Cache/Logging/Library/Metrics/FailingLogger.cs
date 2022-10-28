// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.Logging
{
    /// <summary>
    /// A base logging implementation that fails all the operations.
    /// </summary>
    public abstract class FailingLogger : ILogger
    {
        /// <inheritdoc />
        public virtual void Dispose()
        {
            // Throw is the only exception and should not throw.
        }

        /// <inheritdoc />
        public Severity CurrentSeverity => throw new NotImplementedException();

        /// <inheritdoc />
        public int ErrorCount => throw new NotImplementedException();

        /// <inheritdoc />
        public void Flush()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public void Always(string messageFormat, params object[] messageArgs)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public void Fatal(string messageFormat, params object[] messageArgs)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public void Error(string messageFormat, params object[] messageArgs)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public void Error(Exception exception, string messageFormat, params object[] messageArgs)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public void ErrorThrow(Exception exception, string messageFormat, params object[] messageArgs)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public void Warning(string messageFormat, params object[] messageArgs)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public void Info(string messageFormat, params object[] messageArgs)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public void Debug(string messageFormat, params object[] messageArgs)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public void Debug(Exception exception)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public void Diagnostic(string messageFormat, params object[] messageArgs)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public void Log(Severity severity, string message)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public void LogFormat(Severity severity, string messageFormat, params object[] messageArgs)
        {
            throw new NotImplementedException();
        }
    }
}
