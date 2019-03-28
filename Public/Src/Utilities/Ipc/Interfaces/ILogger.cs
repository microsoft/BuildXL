// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Ipc.Interfaces
{
    /// <summary>
    /// Log levels
    /// </summary>
    public enum LogLevel
    {
        /// <nodoc />
        Info,

        /// <nodoc />
        Verbose,

        /// <nodoc />
        Warning,

        /// <nodoc />
        Error
    }

    /// <summary>
    /// Simple logging interface for classes implementing
    /// <see cref="IClient"/> and <see cref="IServer"/> to use.
    /// </summary>
    public interface ILogger : IDisposable
    {
        /// <nodoc />
        void Log(LogLevel level, string format, params object[] args);
    }
}
