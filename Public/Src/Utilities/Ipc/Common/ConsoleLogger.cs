// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using BuildXL.Ipc.Interfaces;

namespace BuildXL.Ipc.Common
{
    /// <summary>
    /// Implementation of <see cref="IIpcLogger "/> that forwards log requests to System.Console.
    /// </summary>
    public sealed class ConsoleLogger : IIpcLogger
    {
        /// <nodoc />
        public bool IsLoggingVerbose { get; }

        /// <nodoc />
        public string Prefix { get; }

        /// <param name="logVerbose">Whether it should log verbose messages.</param>
        /// <param name="prefix">Optional prefix to prepend to every log message.</param>
        public ConsoleLogger(bool logVerbose, string prefix = null)
        {
            IsLoggingVerbose = logVerbose;
            Prefix = prefix ?? string.Empty;
        }

        /// <summary>
        /// Logs to Console.Out, unless <paramref name="level"/> is <see cref="Interfaces.LogLevel.Verbose"/>,
        /// and <see cref="IsLoggingVerbose"/> is <code>false</code>.
        /// </summary>
        public void Log(LogLevel level, string format, params object[] args)
        {
            if (!IsLoggingVerbose && level == LogLevel.Verbose)
            {
                return;
            }

            var message = Prefix + LoggerExtensions.Format(level, format, args);
            var writer = level == LogLevel.Error ? Console.Error : Console.Out;
            writer.WriteLine(message);
        }

        /// <summary>
        /// Logs to Console.Out, unless <paramref name="level"/> is <see cref="Interfaces.LogLevel.Verbose"/>,
        /// and <see cref="IsLoggingVerbose"/> is <code>false</code>.
        /// </summary>
        public void Log(LogLevel level, StringBuilder message)
        {
            if (!IsLoggingVerbose && level == LogLevel.Verbose)
            {
                return;
            }

            LoggerExtensions.Format(level, message).Insert(0, Prefix);
            var writer = level == LogLevel.Error ? Console.Error : Console.Out;
            writer.WriteLine(message);
        }

        /// <summary>
        /// Not supported.
        /// </summary>
        public void Log(LogLevel level, string header, IEnumerable<string> items, bool placeItemsOnSeparateLines) => throw new NotSupportedException();

        /// <nodoc />
        public void Dispose() { }
    }
}
