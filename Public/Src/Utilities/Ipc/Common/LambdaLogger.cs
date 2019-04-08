// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Ipc.Interfaces;

namespace BuildXL.Ipc.Common
{
    /// <summary>
    /// Logger that receives an action to which it delegates all log requests.
    /// </summary>
    public sealed class LambdaLogger : ILogger
    {
        private Action<LogLevel, string, object[]> m_logFunction;

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
