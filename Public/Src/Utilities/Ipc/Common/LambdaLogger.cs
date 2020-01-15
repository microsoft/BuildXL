// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Ipc.Interfaces;

namespace BuildXL.Ipc.Common
{
    /// <summary>
    /// Logger that receives an action to which it delegates all log requests.
    /// </summary>
    public sealed class LambdaLogger : ILogger
    {
        private readonly Action<LogLevel, string, object[]> m_logFunction;

        /// <nodoc />
        public LambdaLogger(Action<LogLevel, string, object[]> logFunction)
        {
            m_logFunction = logFunction;
        }

        /// <inheritdoc />
        public void Log(LogLevel level, string format, params object[] args) => m_logFunction(level, format, args);

        /// <nodoc />
        public void Dispose() { }
    }
}
