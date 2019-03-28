// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Interfaces.Logging
{
    /// <summary>
    ///     An interface for logging messages with severity.
    /// </summary>
    public interface ILogger : IDisposable
    {
        /// <summary>
        ///     Gets give the current lowest severity being logged by the current
        ///     collection of attached logs. This can be used to avoid high
        ///     compute that would be wasted if logging a message that would
        ///     be filtered.
        /// </summary>
        Severity CurrentSeverity { get; }

        /// <summary>
        ///     Gets number log messages at Error severity
        /// </summary>
        int ErrorCount { get; }

        /// <summary>
        ///     Flush buffered messages to storage.
        /// </summary>
        void Flush();

        /// <summary>
        ///     Log a message if current severity is set to at least Always.
        /// </summary>
        /// <param name="messageFormat">Format string</param>
        /// <param name="messageArgs">Zero or more objects to format</param>
        void Always(string messageFormat, params object[] messageArgs);

        /// <summary>
        ///     Log a message if current severity is set to Fatal.
        /// </summary>
        /// <param name="messageFormat">Format string</param>
        /// <param name="messageArgs">Zero or more objects to format</param>
        void Fatal(string messageFormat, params object[] messageArgs);

        /// <summary>
        ///     Log a message if current severity is set to at least Error.
        /// </summary>
        /// <param name="messageFormat">Format string</param>
        /// <param name="messageArgs">Zero or more objects to format</param>
        void Error(string messageFormat, params object[] messageArgs);

        /// <summary>
        ///     Log an exception and message if current severity is set to at least Error.
        /// </summary>
        /// <param name="exception">Captured exception to be logged</param>
        /// <param name="messageFormat">Format string</param>
        /// <param name="messageArgs">Zero or more objects to format</param>
        void Error(Exception exception, string messageFormat, params object[] messageArgs);

        /// <summary>
        ///     Log an exception and message if current severity is set to at least Error and throw unconditionally.
        /// </summary>
        /// <param name="exception">Captured exception to be logged</param>
        /// <param name="messageFormat">Format string</param>
        /// <param name="messageArgs">Zero or more objects to format</param>
        void ErrorThrow(Exception exception, string messageFormat, params object[] messageArgs);

        /// <summary>
        ///     Log a message if current severity is set to at least Warning.
        /// </summary>
        /// <param name="messageFormat">Format string</param>
        /// <param name="messageArgs">Zero or more objects to format</param>
        void Warning(string messageFormat, params object[] messageArgs);

        /// <summary>
        ///     Log a message if current severity is set to at least Info.
        /// </summary>
        /// <param name="messageFormat">Format string</param>
        /// <param name="messageArgs">Zero or more objects to format</param>
        void Info(string messageFormat, params object[] messageArgs);

        /// <summary>
        ///     Log a message if current severity is set to at least Debug.
        /// </summary>
        /// <param name="messageFormat">Format string</param>
        /// <param name="messageArgs">Zero or more objects to format</param>
        void Debug(string messageFormat, params object[] messageArgs);

        /// <summary>
        ///     Log an exception if current severity is set to at least Debug.
        /// </summary>
        /// <param name="exception">Captured exception to be logged</param>
        void Debug(Exception exception);

        /// <summary>
        ///     Log a message if current severity is set to at least Diagnostic.
        /// </summary>
        /// <param name="messageFormat">Format string</param>
        /// <param name="messageArgs">Zero or more objects to format</param>
        void Diagnostic(string messageFormat, params object[] messageArgs);

        /// <summary>
        ///     Log a message with the given severity if it is at least as high as the current severity.
        /// </summary>
        /// <param name="severity">Severity to attach to this log message.</param>
        /// <param name="message">The raw string to log</param>
        void Log(Severity severity, string message);

        /// <summary>
        ///     Log a message with the given severity if it is at least as high as the current severity.
        /// </summary>
        /// <param name="severity">Severity to attach to this log message.</param>
        /// <param name="messageFormat">Format string</param>
        /// <param name="messageArgs">Zero or more objects to format</param>
        void LogFormat(Severity severity, string messageFormat, params object[] messageArgs);
    }
}
