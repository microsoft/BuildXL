// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;

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
    public interface IIpcLogger : IDisposable
    {
        /// <nodoc />
        void Log(LogLevel level, string format, params object[] args);

        /// <nodoc />
        void Log(LogLevel level, StringBuilder message);

        /// <nodoc />
        void Log(LogLevel level, string header, IEnumerable<string> items, bool placeItemsOnSeparateLines);
    }
}
